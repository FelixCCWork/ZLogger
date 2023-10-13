using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Utf8StringInterpolation;
using ZLogger.Internal;

namespace ZLogger
{
    public readonly struct InterpolatedStringLogState : IZLoggerFormattable, IDisposable
    {
        static InterpolatedStringLogState()
        {
            LogEntryFactory<InterpolatedStringLogState>.Create = CreateEntry;
        }

        static IZLoggerEntry CreateEntry(in LogInfo logInfo, in InterpolatedStringLogState logState)
        {
            return ZLoggerEntry<InterpolatedStringLogState>.Create(logInfo, logState);
        }

        public int ParameterCount => arguments.Length;
        public bool IsSupportUtf8ParameterKey => false;

        readonly KeyValuePair<string, object?>[] arguments; // TODO: avoid boxing!
        readonly ArrayBufferWriter<byte> buffer;

        public InterpolatedStringLogState(KeyValuePair<string, object?>[] arguments, ArrayBufferWriter<byte> buffer)
        {
            this.arguments = arguments;
            this.buffer = buffer;
        }

        // TODO: There is no timing to dispose of the state struct!
        public void Dispose()
        {
            ArrayBufferWriterPool.Return(buffer);
        }

        public override string ToString()
        {
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        
        public void ToString(IBufferWriter<byte> writer)
        {
            var written = buffer.WrittenSpan;
            var dest = writer.GetSpan(written.Length);
            written.CopyTo(dest);
            writer.Advance(written.Length);
        }

        public void WriteJsonParameterKeyValues(Utf8JsonWriter jsonWriter, JsonSerializerOptions jsonSerializerOptions)
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                var (key, value) = arguments[i];
                jsonWriter.WritePropertyName(key);
                if (value == null)
                {
                    jsonWriter.WriteNullValue();
                }
                else
                {
                    var valueType = GetParameterType(i);
                    JsonSerializer.Serialize(jsonWriter, value, valueType, jsonSerializerOptions); // TODO: more optimize ?
                }
            }
        }

        public ReadOnlySpan<byte> GetParameterKey(int index)
        {
            throw new NotSupportedException();
        }

        public string GetParameterKeyAsString(int index)
        {
            return arguments[index].Key;
        }

        public object? GetParameterValue(int index)
        {
            return arguments[index].Value;
        }

        public T? GetParameterValue<T>(int index)
        {
            return (T?)arguments[index].Value;
        }

        public Type GetParameterType(int index)
        {
            return arguments[index].Value?.GetType() ?? typeof(string);
        }
    }
    
    [InterpolatedStringHandler]
    public ref struct ZLoggerInterpolatedStringHandler
    {
        int i;
        readonly KeyValuePair<string, object?>[] arguments; // TODO: avoid boxing!
        readonly ArrayBufferWriter<byte> buffer;
        Utf8StringWriter<ArrayBufferWriter<byte>> utf8StringWriter;

        public ZLoggerInterpolatedStringHandler(int literalLength, int formattedCount)
        {
            i = 0;
            arguments = new KeyValuePair<string, object?>[formattedCount];
            buffer = ArrayBufferWriterPool.Rent();
            utf8StringWriter = new Utf8StringWriter<ArrayBufferWriter<byte>>(literalLength, formattedCount, buffer);
        }

        public void AppendLiteral(string s)
        {
            utf8StringWriter.AppendLiteral(s);
        }

        public void AppendFormatted<T>(T value, int alignment = 0, string? format = null, [CallerArgumentExpression("value")] string? argumentName = null)
        {
            utf8StringWriter.AppendFormatted(value, alignment, format);
            arguments[i++] = new KeyValuePair<string, object?>(argumentName ?? $"Arg{i}", value);
        }

        public InterpolatedStringLogState GetState()
        {
            utf8StringWriter.Flush();
            return new InterpolatedStringLogState(arguments, buffer);
        }
    }    
}

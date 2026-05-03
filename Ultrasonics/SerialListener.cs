using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Security.RightsManagement;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    internal class SerialListener
    {
        public static string[] AvailablePorts => SerialPort.GetPortNames();

        volatile bool _disconnectRequested;

        ConcurrentDictionary<Stream, ConcurrentQueue<(byte[] data, byte writeType)>> _writeQueue =
            new ConcurrentDictionary<Stream, ConcurrentQueue<(byte[] data, byte writeType)>>();

        string _portName;
        public string PortName => _portName;

        public Exception? LastException { get; private set; }
        public DateTimeOffset LastExceptionTime { get; private set; }

        public event EventHandler<SerialPacket>? PacketReceived;
        public event EventHandler? Stopped;

        public bool IsRunning { get; private set; }

        public SerialListener(string portName)
        {
            _portName = portName;
            var port = new SerialPort(_portName);
            port.ReadTimeout = 100;
            port.Open();
            _writeQueue[port.BaseStream] = new ConcurrentQueue<(byte[] data, byte writeType)>();
            Task t = new Task(() => MainLoop(port), default, TaskCreationOptions.LongRunning);
            t.Start();
            _baseStream = port.BaseStream;
            WriteStream((byte)'B');
            IsRunning = true;
        }

        Stream _baseStream;

        public void Stop()
        {
            _disconnectRequested = true;
        }

        public void WriteStream(params byte[] data)
        {
            _writeQueue[_baseStream].Enqueue((data, (byte)0));
        }


        void MainLoop(SerialPort port)
        {
            try
            {
                ReadStream(port.BaseStream);
            }
            catch (Exception ex)
            {
                LastException = ex;
                LastExceptionTime = DateTimeOffset.Now;
            }
            finally
            {
                IsRunning = false;
                port.Dispose();
                Stopped?.Invoke(this, new EventArgs());
            }
        }

        enum ReadState { ReadingPacket, WaitingForSize }

        const int EdgeCaptureCount = 20;
        
        void ReadStream(Stream stream)
        {
            uint last4Bytes = 0;

            var reader = new BinaryReader(stream);

            for (var cur = ReadOrWriteStream(stream); cur != -1; cur = ReadOrWriteStream(stream))
            {
                try
                {
                    var curByte = (byte)cur;
                    last4Bytes >>= 8;
                    last4Bytes |= ((uint)cur) << 24;
                    if (last4Bytes == 0xDEADBEEF)
                    {
                        last4Bytes = 0;
                        var packetSize = reader.ReadUInt16();
                        byte progId = reader.ReadByte(); // Debug parameter, Discarded
                        var outputChannel = reader.ReadByte();
                        var inputChannel = reader.ReadByte();
                        var excitationCount = reader.ReadByte();
                        var temperature = reader.ReadInt16();
                        // 12 header bytes:
                        // 4 for DEADBEEF, 2 for size, 1 for progid, 1 for out, 1 for in, 1 for excitation count,
                        // 2 for temperature
                        int edgeCaptureSize = EdgeCaptureCount * 2;
                        int bufferSize = packetSize - 12 - edgeCaptureSize;
                        var trace = new short[bufferSize / 2];
                        var traceBytes = reader.ReadBytes(bufferSize);
                        Buffer.BlockCopy(traceBytes, 0, trace, 0, bufferSize);

                        var edgeBytes = reader.ReadBytes(edgeCaptureSize);
                        var edgeCaptures = new short[EdgeCaptureCount];
                        Buffer.BlockCopy(edgeBytes, 0, edgeCaptures, 0, edgeCaptureSize);

                        var packet = new SerialPacket()
                        {
                            OutputChannel = outputChannel,
                            InputChannel = inputChannel,
                            ExcitationCount = excitationCount,
                            Temperature_x10 = temperature,
                            Trace = trace,
                            Timestamp = DateTimeOffset.Now,
                            EdgeCaptures = edgeCaptures
                        };

                        Task.Run(() => PacketReceived?.Invoke(this, packet));
                    }
                }
                catch (Exception ex)
                {
                    LastException = ex;
                    LastExceptionTime = DateTimeOffset.Now;
                }
            }
        }

        int ReadOrWriteStream(Stream stream)
        {
            while (true)
            {
                try
                {
                    if (_disconnectRequested)
                        return -1;
                    return stream.ReadByte();
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException)
                {
                    if (_disconnectRequested)
                        return -1;
                    while (_writeQueue[stream].TryDequeue(out var toWrite))
                    {
                        WriteStreamInternal(stream, toWrite.data, toWrite.writeType);
                    }
                }
            }
        }

        private void WriteStreamInternal(Stream stream, byte[] data, byte writeType)
        {
            lock (stream)
            {
                using BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, true);
                writer.Write(data);
            }
        }

    }
}

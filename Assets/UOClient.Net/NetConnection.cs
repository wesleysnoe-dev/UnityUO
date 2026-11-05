using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace UOClient.Net
{
    /// <summary>
    /// One TCP connection to a login or game server. A background thread reads, decompresses
    /// (when enabled) and frames packets into a queue; the owner drains it on its own thread with
    /// <see cref="TryDequeue"/>, so game state is only ever touched on that thread.
    /// </summary>
    public sealed class NetConnection : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly short[] _lengths;
        private readonly ConcurrentQueue<byte[]> _incoming = new ConcurrentQueue<byte[]>();
        private readonly Thread _reader;
        private readonly object _sendLock = new object();
        private readonly Huffman _huffman = new Huffman();
        private volatile bool _compressed;
        private volatile bool _closed;
        private readonly Action<byte[]> _offline;

        // Framing buffer (decompressed bytes not yet forming a whole packet). Reader thread only.
        private byte[] _pending = new byte[0x10000];
        private int _pendingLength;

        public NetConnection(string host, int port, ClientVersion version, int timeoutMs = 5000)
        {
            _lengths = PacketLengths.For(version);
            _tcp = new TcpClient { NoDelay = true };
            IAsyncResult connect = _tcp.BeginConnect(host, port, null, null);
            if (!connect.AsyncWaitHandle.WaitOne(timeoutMs) || !_tcp.Connected)
            {
                _tcp.Close();
                throw new IOException($"Could not connect to {host}:{port} within {timeoutMs} ms.");
            }
            _tcp.EndConnect(connect);
            _stream = _tcp.GetStream();
            Endpoint = $"{host}:{port}";

            _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"UO net {Endpoint}" };
            _reader.Start();
        }

        private NetConnection(ClientVersion version, Action<byte[]> sent)
        {
            _lengths = PacketLengths.For(version);
            _offline = sent;
            Endpoint = "offline";
        }

        /// <summary>A connection to no server (tests and packet replays): what is sent goes to <paramref name="sent"/>, nothing arrives.</summary>
        public static NetConnection Offline(ClientVersion version, Action<byte[]> sent) => new NetConnection(version, sent);

        public string Endpoint { get; }
        public bool IsConnected => !_closed;

        /// <summary>Why the connection ended (set once it has).</summary>
        public string CloseReason { get; private set; }

        /// <summary>Optional hook for every packet in (true) or out (false); called on the reader or sender thread.</summary>
        public Action<bool, byte[]> PacketLogger { get; set; }

        /// <summary>From now on, incoming bytes are Huffman-decoded (the game server compresses after game login).</summary>
        public void EnableCompression()
        {
            _huffman.Reset();
            _compressed = true;
        }

        public void Send(byte[] packet) => SendRaw(packet, true);

        /// <summary>Raw bytes that are not a packet (the 4-byte seed on the game connection).</summary>
        public void SendRaw(byte[] bytes, bool isPacket = false)
        {
            if (_closed)
                return;
            if (_offline != null)
            {
                if (isPacket)
                    _offline(bytes);
                return;
            }
            try
            {
                lock (_sendLock)
                    _stream.Write(bytes, 0, bytes.Length);
                if (isPacket)
                    PacketLogger?.Invoke(false, bytes);
            }
            catch (Exception e)
            {
                Close($"send failed: {e.Message}");
            }
        }

        public bool TryDequeue(out byte[] packet) => _incoming.TryDequeue(out packet);

        private void ReadLoop()
        {
            var raw = new byte[0x4000];
            var decoded = new byte[0x4000 * 4];
            try
            {
                while (!_closed)
                {
                    int n = _stream.Read(raw, 0, raw.Length);
                    if (n <= 0)
                    {
                        Close("server closed the connection");
                        return;
                    }

                    if (_compressed)
                    {
                        int d = _huffman.Decompress(raw.AsSpan(0, n), decoded);
                        Append(decoded, d);
                    }
                    else
                    {
                        Append(raw, n);
                    }
                    Frame();
                }
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
            {
                Close(_closed ? CloseReason : $"connection lost: {e.Message}");
            }
            catch (Exception e)
            {
                Close($"reader crashed: {e.GetType().Name}: {e.Message}");
            }
        }

        private void Append(byte[] src, int count)
        {
            if (_pendingLength + count > _pending.Length)
                Array.Resize(ref _pending, Math.Max(_pending.Length * 2, _pendingLength + count));
            Buffer.BlockCopy(src, 0, _pending, _pendingLength, count);
            _pendingLength += count;
        }

        private void Frame()
        {
            int offset = 0;
            while (offset < _pendingLength)
            {
                byte id = _pending[offset];
                int length = _lengths[id];
                if (length < 0)
                {
                    if (_pendingLength - offset < 3)
                        break;
                    length = BinaryPrimitives.ReadUInt16BigEndian(_pending.AsSpan(offset + 1));
                    if (length < 3)
                    {
                        Close($"bad length {length} for variable packet 0x{id:X2}");
                        return;
                    }
                }
                else if (length == 0)
                {
                    Close($"unknown packet 0x{id:X2}");
                    return;
                }

                if (_pendingLength - offset < length)
                    break;

                var packet = new byte[length];
                Buffer.BlockCopy(_pending, offset, packet, 0, length);
                offset += length;
                PacketLogger?.Invoke(true, packet);
                _incoming.Enqueue(packet);
            }

            if (offset > 0)
            {
                Buffer.BlockCopy(_pending, offset, _pending, 0, _pendingLength - offset);
                _pendingLength -= offset;
            }
        }

        public void Close(string reason)
        {
            if (_closed)
                return;
            CloseReason = reason;
            _closed = true;
            try { _tcp?.Close(); } catch { /* already closed */ }
        }

        public void Dispose() => Close("closed by client");

        public bool IsVariable(byte id) => _lengths[id] < 0;
    }
}

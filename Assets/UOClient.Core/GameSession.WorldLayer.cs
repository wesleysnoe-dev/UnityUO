using System;
using System.Collections.Generic;
using UOClient.Net;
using UOClient.WorldLayer;

namespace UOClient.Core
{
    /// <summary>
    /// The world editor's messages (WORLD_EDITOR.md; payloads in UOClient.WorldLayer.WorldProtocol): only sent after
    /// <see cref="WorldHello"/>, which the Unity side calls under -worldeditor, so a server without the editor never sees
    /// them. Painted regions arrive in parts and are handed on whole (<see cref="WorldRegionReceived"/>).
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>The server answered the hello: whether this account may edit.</summary>
        public event Action<bool> WorldEditorReady;

        /// <summary>A region (map, rx, ry, version, its serialized bytes; null bytes: nobody painted it).</summary>
        public event Action<int, int, int, int, byte[]> WorldRegionReceived;

        /// <summary>A region changed on the server (map, rx, ry, version): ask for it again if it is in view.</summary>
        public event Action<int, int, int, int> WorldRegionChanged;

        /// <summary>The server's answer to an edit (ok, message).</summary>
        public event Action<bool, string> WorldEditResult;

        /// <summary>Whether the hello went out (the editor's messages are only sent after it).</summary>
        public bool WorldLayerOn { get; private set; }

        /// <summary>Whether the server said this account may edit.</summary>
        public bool CanEditWorld { get; private set; }

        private readonly Dictionary<(int, int, int), (int Version, byte[] Data, int Got)> _regionParts =
            new Dictionary<(int, int, int), (int, byte[], int)>();

        public void WorldHello()
        {
            if (State != SessionState.InWorld)
                return;
            WorldLayerOn = true;
            _conn.Send(new PacketWriter(0xBF).UInt16(WorldProtocol.Hello).Int32(WorldProtocol.ProtocolVersion).ToArray());
        }

        public void RequestWorldRegion(int map, int rx, int ry, int haveVersion)
        {
            if (State == SessionState.InWorld && WorldLayerOn)
                _conn.Send(new PacketWriter(0xBF).UInt16(WorldProtocol.RequestRegion).Byte(map).UInt16(rx).UInt16(ry).Int32(haveVersion).ToArray());
        }

        public void SendWorldStroke(int map, in BrushStroke stroke)
        {
            if (State != SessionState.InWorld || !WorldLayerOn)
                return;
            var w = new PacketWriter(0xBF).UInt16(WorldProtocol.Stroke).Byte(map);
            foreach (byte b in WorldProtocol.StrokeBytes(stroke))
                w.Byte(b);
            _conn.Send(w.ToArray());
        }

        /// <summary>Closes one mouse drag of strokes into one undo step.</summary>
        public void EndWorldStroke()
        {
            if (State == SessionState.InWorld && WorldLayerOn)
                _conn.Send(new PacketWriter(0xBF).UInt16(WorldProtocol.EndStroke).ToArray());
        }

        public void UndoWorld(bool redo)
        {
            if (State == SessionState.InWorld && WorldLayerOn)
                _conn.Send(new PacketWriter(0xBF).UInt16(WorldProtocol.Undo).Bool(redo).ToArray());
        }

        /// <summary>The server placed an object for this editor (its serial), so it can be selected.</summary>
        public event Action<uint> WorldObjectPlaced;

        /// <summary>Places a static object (the OBJECTS tab); the server answers with <see cref="WorldObjectPlaced"/>.</summary>
        public void PlaceWorldObject(int map, int graphic, int x, int y, int z, int hue)
        {
            if (State == SessionState.InWorld && WorldLayerOn)
                _conn.Send(new PacketWriter(0xBF).UInt16(WorldProtocol.PlaceObject).Byte(map).UInt16(graphic).UInt16(x).UInt16(y)
                    .Byte((sbyte)Math.Max(-128, Math.Min(127, z))).UInt16(hue).ToArray());
        }

        /// <summary>Moves a placed static object to (x, y, z).</summary>
        public void MoveWorldObject(uint serial, int x, int y, int z) => ObjectOp(serial, WorldProtocol.ObjectMove, x, y, z);

        /// <summary>Deletes a placed static object.</summary>
        public void DeleteWorldObject(uint serial) => ObjectOp(serial, WorldProtocol.ObjectDelete, 0, 0, 0);

        private void ObjectOp(uint serial, byte op, int x, int y, int z)
        {
            if (State == SessionState.InWorld && WorldLayerOn)
                _conn.Send(new PacketWriter(0xBF).UInt16(WorldProtocol.ObjectOp).UInt32(serial).Byte(op).UInt16(x).UInt16(y)
                    .Byte((sbyte)Math.Max(-128, Math.Min(127, z))).ToArray());
        }

        /// <summary>0xBF world layer sub-commands; true if consumed. Ignored until the hello went out.</summary>
        private bool WorldLayerExtended(int sub, PacketReader r)
        {
            if (!WorldLayerOn)
                return false;
            switch (sub)
            {
                case WorldProtocol.HelloReply:
                {
                    int version = r.ReadInt32();
                    CanEditWorld = r.ReadBool() && version == WorldProtocol.ProtocolVersion;
                    WorldEditorReady?.Invoke(CanEditWorld);
                    return true;
                }
                case WorldProtocol.RegionData:
                {
                    int map = r.ReadByte(), rx = r.ReadUInt16(), ry = r.ReadUInt16(), version = r.ReadInt32();
                    int total = r.ReadInt32(), offset = r.ReadInt32();
                    var key = (map, rx, ry);
                    if (total <= 0)
                    {
                        _regionParts.Remove(key);
                        WorldRegionReceived?.Invoke(map, rx, ry, version, null);
                        return true;
                    }
                    if (!_regionParts.TryGetValue(key, out var parts) || parts.Version != version || parts.Data.Length != total || offset == 0)
                        parts = (version, new byte[total], 0);
                    int n = Math.Min(r.Remaining, total - offset);
                    for (int i = 0; i < n; i++)
                        parts.Data[offset + i] = r.ReadByte();
                    parts.Got += n;
                    if (parts.Got >= total)
                    {
                        _regionParts.Remove(key);
                        WorldRegionReceived?.Invoke(map, rx, ry, version, parts.Data);
                    }
                    else
                        _regionParts[key] = parts;
                    return true;
                }
                case WorldProtocol.Result:
                {
                    bool ok = r.ReadBool();
                    int len = r.ReadByte();
                    var chars = new char[Math.Min(len, r.Remaining)];
                    for (int i = 0; i < chars.Length; i++)
                        chars[i] = (char)r.ReadByte();
                    WorldEditResult?.Invoke(ok, new string(chars));
                    return true;
                }
                case WorldProtocol.RegionChanged:
                {
                    int map = r.ReadByte(), rx = r.ReadUInt16(), ry = r.ReadUInt16(), version = r.ReadInt32();
                    WorldRegionChanged?.Invoke(map, rx, ry, version);
                    return true;
                }
                case WorldProtocol.ObjectPlaced:
                    WorldObjectPlaced?.Invoke(r.ReadUInt32());
                    return true;
            }
            return false;
        }
    }
}

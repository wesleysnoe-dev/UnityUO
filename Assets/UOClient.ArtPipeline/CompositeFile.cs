using System.Collections.Generic;
using System.IO;
using System.Text;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO.Formats;

namespace UOClient.ArtPipeline
{
    /// <summary>
    /// On-disk form of a <see cref="Composite"/>, one folder per region:
    ///   composite.png  - the render to send to ComfyUI
    ///   ownership.bin  - int32 width, int32 height, then int32 item index per pixel (-1 = empty)
    ///   items.bin      - draw items, read back by the slicer
    ///   items.json     - the same items, for people and other tools
    ///   ownership.png  - debug coloring of the ownership map
    /// The restyled image is expected next to them as restyled.png unless given explicitly.
    /// </summary>
    public static class CompositeFile
    {
        public const string ImageName = "composite.png";
        public const string RestyledName = "restyled.png";

        private const uint ItemsMagic = 0x49434F55; // "UOCI"
        private const int ItemsVersion = 2; // 2: placement ordinal, image origin

        public static void Save(string dir, Composite c)
        {
            Directory.CreateDirectory(dir);
            Png.Write(Path.Combine(dir, ImageName), c.Image);
            Png.Write(Path.Combine(dir, "ownership.png"), OwnershipPreview(c));

            using (var bw = new BinaryWriter(File.Create(Path.Combine(dir, "ownership.bin"))))
            {
                bw.Write(c.Image.Width);
                bw.Write(c.Image.Height);
                foreach (int o in c.Owner)
                    bw.Write(o);
            }

            using (var bw = new BinaryWriter(File.Create(Path.Combine(dir, "items.bin"))))
            {
                bw.Write(ItemsMagic);
                bw.Write(ItemsVersion);
                bw.Write(c.Map);
                bw.Write(c.RegionX);
                bw.Write(c.RegionY);
                bw.Write(c.RegionWidth);
                bw.Write(c.RegionHeight);
                bw.Write(c.Scale);
                bw.Write(c.OriginX);
                bw.Write(c.OriginY);
                bw.Write(c.Items.Count);
                foreach (DrawItem it in c.Items)
                {
                    bw.Write((byte)it.Kind);
                    bw.Write(it.Graphic);
                    bw.Write(it.X);
                    bw.Write(it.Y);
                    bw.Write(it.Z);
                    bw.Write(it.Hue);
                    bw.Write(it.PriorityZ);
                    bw.Write(it.Sequence);
                    bw.Write(it.Ordinal);
                    bw.Write(it.Stretched);
                    bw.Write(it.ZRight);
                    bw.Write(it.ZBottom);
                    bw.Write(it.ZLeft);
                    bw.Write(it.Left);
                    bw.Write(it.Top);
                    bw.Write(it.Width);
                    bw.Write(it.Height);
                }
            }

            File.WriteAllText(Path.Combine(dir, "items.json"), ItemsJson(c));
        }

        public static Composite Load(string dir)
        {
            Bitmap32 image = Png.Read(Path.Combine(dir, ImageName));

            int[] owner;
            using (var br = new BinaryReader(File.OpenRead(Path.Combine(dir, "ownership.bin"))))
            {
                int w = br.ReadInt32(), h = br.ReadInt32();
                if (w != image.Width || h != image.Height)
                    throw new InvalidDataException($"ownership.bin is {w}x{h} but {ImageName} is {image.Width}x{image.Height}.");
                owner = new int[w * h];
                for (int i = 0; i < owner.Length; i++)
                    owner[i] = br.ReadInt32();
            }

            using (var br = new BinaryReader(File.OpenRead(Path.Combine(dir, "items.bin"))))
            {
                if (br.ReadUInt32() != ItemsMagic || br.ReadInt32() != ItemsVersion)
                    throw new InvalidDataException("items.bin is missing or from an incompatible version; re-run compose.");

                int map = br.ReadInt32(), rx = br.ReadInt32(), ry = br.ReadInt32(), rw = br.ReadInt32(), rh = br.ReadInt32();
                int scale = br.ReadInt32();
                int originX = br.ReadInt32(), originY = br.ReadInt32();
                int count = br.ReadInt32();
                var items = new List<DrawItem>(count);
                for (int i = 0; i < count; i++)
                {
                    items.Add(new DrawItem
                    {
                        Kind = (DrawKind)br.ReadByte(),
                        Graphic = br.ReadUInt16(),
                        X = br.ReadInt32(),
                        Y = br.ReadInt32(),
                        Z = br.ReadSByte(),
                        Hue = br.ReadUInt16(),
                        PriorityZ = br.ReadInt32(),
                        Sequence = br.ReadInt32(),
                        Ordinal = br.ReadByte(),
                        Stretched = br.ReadBoolean(),
                        ZRight = br.ReadSByte(),
                        ZBottom = br.ReadSByte(),
                        ZLeft = br.ReadSByte(),
                        Left = br.ReadInt32(),
                        Top = br.ReadInt32(),
                        Width = br.ReadInt32(),
                        Height = br.ReadInt32()
                    });
                }

                return new Composite(image, owner, items)
                {
                    Map = map,
                    RegionX = rx,
                    RegionY = ry,
                    RegionWidth = rw,
                    RegionHeight = rh,
                    Scale = scale,
                    OriginX = originX,
                    OriginY = originY
                };
            }
        }

        public static Bitmap32 OwnershipPreview(Composite c)
        {
            var bmp = new Bitmap32(c.Image.Width, c.Image.Height);
            for (int i = 0; i < c.Owner.Length; i++)
            {
                int o = c.Owner[i];
                if (o < 0)
                    continue;
                uint h = (uint)o * 2654435761u; // spread neighboring indices to distinct colors
                bmp.Pixels[i] = 0xFF000000 | (h & 0x00FFFFFF) | 0x00404040;
            }
            return bmp;
        }

        private static string ItemsJson(Composite c)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"map\": {c.Map}, \"x\": {c.RegionX}, \"y\": {c.RegionY}, \"width\": {c.RegionWidth}, \"height\": {c.RegionHeight},\n");
            sb.Append($"  \"scale\": {c.Scale}, \"imageWidth\": {c.Image.Width}, \"imageHeight\": {c.Image.Height}, \"originX\": {c.OriginX}, \"originY\": {c.OriginY},\n");
            sb.Append("  \"items\": [\n");
            for (int i = 0; i < c.Items.Count; i++)
            {
                DrawItem it = c.Items[i];
                sb.Append($"    {{ \"index\": {i}, \"kind\": \"{it.Kind}\", \"graphic\": {it.Graphic}, \"tileX\": {it.X}, \"tileY\": {it.Y}, \"z\": {it.Z}, ")
                  .Append($"\"hue\": {it.Hue}, \"ordinal\": {it.Ordinal}, \"stretched\": {(it.Stretched ? "true" : "false")}, ")
                  .Append($"\"left\": {it.Left}, \"top\": {it.Top}, \"width\": {it.Width}, \"height\": {it.Height} }}")
                  .Append(i < c.Items.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }
    }
}

using System.IO.Compression;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Reads a PNG one row at a time, so an 8k-10k world's splat maps don't
    /// need a 256-400 MB buffer. Skia 2.88's PNG codec only decodes whole images.
    ///
    /// Handles what Unity's EncodeToPNG writes: 8-bit, non-interlaced, grey or
    /// RGB, with or without alpha. Rows come out as RGBA, unpremultiplied, so
    /// colors under alpha 0 survive. Anything else returns null from Open.
    /// </summary>
    public sealed class PngRowReader : IDisposable
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private readonly Stream _file;
        private readonly DeflateStream _inflate;
        private readonly int _channels;
        private byte[] _row;
        private byte[] _prev;

        public int Width { get; }
        public int Height { get; }

        private PngRowReader(Stream file, int width, int height, int channels)
        {
            _file = file;
            Width = width;
            Height = height;
            _channels = channels;
            _row = new byte[width * channels];
            _prev = new byte[width * channels];

            // Image data is zlib: a 2-byte header, then raw deflate.
            var idat = new IdatStream(file);
            if (idat.ReadByte() < 0 || idat.ReadByte() < 0)
                throw new InvalidDataException("PNG has no image data.");
            _inflate = new DeflateStream(idat, CompressionMode.Decompress);
        }

        /// <summary>Opens a PNG this reader can stream, or returns null.</summary>
        public static PngRowReader Open(string path)
        {
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            try
            {
                var sig = new byte[8];
                if (!TryRead(file, sig) || !sig.SequenceEqual(Signature))
                    return Fail(file);

                var header = new byte[8];
                if (!TryRead(file, header) || ReadInt(header, 0) != 13 || Encoding.ASCII.GetString(header, 4, 4) != "IHDR")
                    return Fail(file);

                var ihdr = new byte[13 + 4]; // + CRC
                if (!TryRead(file, ihdr))
                    return Fail(file);

                int width = ReadInt(ihdr, 0), height = ReadInt(ihdr, 4);
                byte depth = ihdr[8], colorType = ihdr[9], interlace = ihdr[12];
                int channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
                if (width <= 0 || height <= 0 || depth != 8 || channels == 0 || interlace != 0)
                    return Fail(file);

                return new PngRowReader(file, width, height, channels);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        /// <summary>Reads the next row, top to bottom, into <paramref name="rgba"/> (Width * 4 bytes).</summary>
        public void ReadRow(byte[] rgba)
        {
            int filter = _inflate.ReadByte();
            if (filter < 0 || !TryRead(_inflate, _row))
                throw new EndOfStreamException("PNG image data ended early.");

            Unfilter(filter);
            ToRgba(rgba);

            (_prev, _row) = (_row, _prev);
        }

        public void Dispose()
        {
            _inflate.Dispose();
            _file.Dispose();
        }

        private void Unfilter(int filter)
        {
            int bpp = _channels;
            var row = _row;
            var prev = _prev;
            switch (filter)
            {
                case 0:
                    break;
                case 1: // Sub
                    for (int i = bpp; i < row.Length; i++)
                        row[i] += row[i - bpp];
                    break;
                case 2: // Up
                    for (int i = 0; i < row.Length; i++)
                        row[i] += prev[i];
                    break;
                case 3: // Average
                    for (int i = 0; i < row.Length; i++)
                        row[i] += (byte)(((i >= bpp ? row[i - bpp] : 0) + prev[i]) / 2);
                    break;
                case 4: // Paeth
                    for (int i = 0; i < row.Length; i++)
                    {
                        int a = i >= bpp ? row[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        row[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                    }
                    break;
                default:
                    throw new InvalidDataException($"Unknown PNG row filter {filter}.");
            }
        }

        private void ToRgba(byte[] rgba)
        {
            var row = _row;
            for (int x = 0, s = 0, d = 0; x < Width; x++, s += _channels, d += 4)
            {
                switch (_channels)
                {
                    case 4:
                        rgba[d] = row[s]; rgba[d + 1] = row[s + 1]; rgba[d + 2] = row[s + 2]; rgba[d + 3] = row[s + 3];
                        break;
                    case 3:
                        rgba[d] = row[s]; rgba[d + 1] = row[s + 1]; rgba[d + 2] = row[s + 2]; rgba[d + 3] = 255;
                        break;
                    case 2:
                        rgba[d] = rgba[d + 1] = rgba[d + 2] = row[s]; rgba[d + 3] = row[s + 1];
                        break;
                    default:
                        rgba[d] = rgba[d + 1] = rgba[d + 2] = row[s]; rgba[d + 3] = 255;
                        break;
                }
            }
        }

        private static PngRowReader Fail(Stream file)
        {
            file.Dispose();
            return null;
        }

        private static int ReadInt(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

        private static bool TryRead(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        /// <summary>
        /// The concatenated data of a PNG's IDAT chunks, skipping the chunks
        /// before them and the headers and CRCs between them.
        /// </summary>
        private sealed class IdatStream : Stream
        {
            private readonly Stream _file;
            private readonly byte[] _header = new byte[8];
            private long _left;
            private bool _inIdat;
            private bool _done;

            public IdatStream(Stream file)
            {
                _file = file;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (_left == 0)
                {
                    if (_done || !NextIdat()) return 0;
                }

                int n = _file.Read(buffer, offset, (int)Math.Min(count, _left));
                if (n <= 0) return 0;
                _left -= n;
                return n;
            }

            // Moves to the next IDAT chunk; false once the IDATs have ended.
            private bool NextIdat()
            {
                while (true)
                {
                    if (_inIdat)
                        _file.Seek(4, SeekOrigin.Current); // CRC of the chunk just read
                    if (!TryRead(_file, _header))
                    {
                        _done = true;
                        return false;
                    }

                    long length = (uint)ReadInt(_header, 0);
                    bool isIdat = _header[4] == 'I' && _header[5] == 'D' && _header[6] == 'A' && _header[7] == 'T';
                    if (isIdat)
                    {
                        _inIdat = true;
                        _left = length;
                        return true;
                    }
                    if (_inIdat)
                    {
                        _done = true; // IDATs are consecutive
                        return false;
                    }
                    _file.Seek(length + 4, SeekOrigin.Current); // a chunk before the image data
                }
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}

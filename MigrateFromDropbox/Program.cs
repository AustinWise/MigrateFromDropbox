using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using HIST_TYPE = System.Single;

namespace MigrateFromDropbox
{
    //Ignore orientation, as the resulting histogram will be the same regardless
    class Histogram : IEquatable<Histogram>
    {
        const int PropertyTagOrientation = 0x0112;

        const int BUCKET_SHIFT = 5;
        const int BUCKET_MASK = (1 << BUCKET_SHIFT) - 1;
        const int BUCKETS = 1 << BUCKET_SHIFT;
        const HIST_TYPE BUCKET_DIFF_TOLERANCE = (HIST_TYPE)0.0001;
        static readonly Vector<HIST_TYPE> sComp = new Vector<HIST_TYPE>(BUCKET_DIFF_TOLERANCE);

        HIST_TYPE[] R, B, G;

        public Histogram(Image img)
        {
            //var orientation = BitConverter.ToInt16(img.GetPropertyItem(PropertyTagOrientation).Value, 0);
            var r = new int[BUCKETS];
            var b = new int[BUCKETS];
            var g = new int[BUCKETS];

            int shift = 8 - BUCKET_SHIFT;

            int width;
            int height;
            using (var bmp = new Bitmap(img))
            {
                //fixOrientation(orientation, bmp);

                width = bmp.Width;
                height = bmp.Height;
                var format = bmp.PixelFormat;

                if (format != PixelFormat.Format32bppArgb)
                    throw new ArgumentOutOfRangeException();

                BitmapData bmpData = null;
                try
                {
                    bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, format);
                    if (width != bmpData.Width || height != bmp.Height)
                        throw new NotSupportedException();

                    unsafe
                    {
                        Int32* ptr = (Int32*)bmpData.Scan0;
                        for (int i = 0; i < width * height; i++)
                        {
                            var c = *ptr;
                            r[(c >> (16 + shift)) & BUCKET_MASK]++;
                            g[(c >> (8 + shift)) & BUCKET_MASK]++;
                            b[(c >> (0 + shift)) & BUCKET_MASK]++;
                            ptr++;
                        }
                    }
                }
                finally
                {
                    if (bmpData != null)
                        bmp.UnlockBits(bmpData);
                }
            }

            HIST_TYPE totalPixles = width * height;
            R = new HIST_TYPE[BUCKETS];
            B = new HIST_TYPE[BUCKETS];
            G = new HIST_TYPE[BUCKETS];
            for (int i = 0; i < BUCKETS; i++)
            {
                R[i] = r[i] / totalPixles;
            }
            for (int i = 0; i < BUCKETS; i++)
            {
                R[i] = r[i] / totalPixles;
            }
            for (int i = 0; i < BUCKETS; i++)
            {
                R[i] = r[i] / totalPixles;
            }
        }

        private static void fixOrientation(short orientation, Bitmap bmp)
        {
            switch (orientation)
            {
                case 1:
                    // No rotation required.
                    break;
                case 2:
                    bmp.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    break;
                case 3:
                    bmp.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    break;
                case 4:
                    bmp.RotateFlip(RotateFlipType.Rotate180FlipX);
                    break;
                case 5:
                    bmp.RotateFlip(RotateFlipType.Rotate90FlipX);
                    break;
                case 6:
                    bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    break;
                case 7:
                    bmp.RotateFlip(RotateFlipType.Rotate270FlipX);
                    break;
                case 8:
                    bmp.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    break;
                default:
                    throw new Exception("unknown orientation");
            }
        }

        static bool IsEqual(HIST_TYPE[] a, HIST_TYPE[] b)
        {
            if (a.Length != BUCKETS || b.Length != BUCKETS)
                throw new ArgumentOutOfRangeException();
            for (int i = 0; i < BUCKETS; i += Vector<HIST_TYPE>.Count)
            {
                var diff = Vector.Subtract(new Vector<HIST_TYPE>(a, i), new Vector<HIST_TYPE>(b, i));
                var abs = Vector.Abs(diff);
                if (Vector.GreaterThanAny(abs, sComp))
                    return false;
            }
            //System.Diagnostics.Debugger.Break();
            return true;
        }

        public bool Equals(Histogram other)
        {
            if (other == null)
                return false;
            return IsEqual(this.R, other.R) && IsEqual(this.B, other.B) && IsEqual(this.G, other.G);
        }
    }
    static class Program
    {
        public const string OLD = @"D:\AustinWise\Dropbox\Camera Uploads";
        public const string NEW = @"C:\Users\AustinWise\OneDrive\Pictures\Camera Roll";

        static void Main(string[] args)
        {
            //DeleteDups.DoDeleteDups();
            //CopyFromDropbox.DoCopy();

            Console.WriteLine("done");
        }
    }

    static class CopyFromDropbox
    {
        public static void DoCopy()
        {
            const int PropertyTagEquipMake = 0x010F;
            const int PropertyTagEquipModel = 0x0110;

            //2011-08-17 14.52.59
            //2016-04-22 01.26.41-1.jpg
            //2016-04-02 16.40.25 HDR-2.jpg
            var rName = new Regex(@"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}.\d{2}.\d{2})(?<hdr> HDR)?(-(?<counter>\d))?.jpg$", RegexOptions.IgnoreCase);

            foreach (var f in Directory.GetFiles(Program.OLD, "*.jpg"))
            {
                var m = rName.Match(Path.GetFileName(f));
                if (!m.Success)
                {
                    throw new NotSupportedException("Unparsable path: " + f);
                }
                var timestamp = DateTime.ParseExact(m.Groups["timestamp"].Value, "yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
                int counter = m.Groups["counter"].Success ? int.Parse(m.Groups["counter"].Value) : 0;
                bool isHdr = m.Groups["hdr"].Success;
                Histogram oldHisto;

                string make = null;
                string model = null;
                using (var img = Image.FromFile(f))
                {
                    oldHisto = new Histogram(img);
                    foreach (var p in img.PropertyItems)
                    {
                        switch (p.Id)
                        {
                            case PropertyTagEquipMake:
                                make = getNullTermString(p);
                                break;
                            case PropertyTagEquipModel:
                                model = getNullTermString(p);
                                break;
                        }
                    }

                    //if (make == null || model == null)
                    //{
                    //    var oldColor = Console.ForegroundColor;
                    //    Console.ForegroundColor = ConsoleColor.Red;
                    //    Console.WriteLine("Could not find make or model: " + f);
                    //    Console.ForegroundColor = oldColor;
                    //    continue;
                    //}
                }

                if (make == null)
                    make = string.Empty;
                else if (make == "Apple")
                    make = "_iOS";
                else
                    make = "_Android";

                var hdrStr = isHdr ? "_HDR" : "";
                var newFileName = $"{timestamp.ToUniversalTime():yyyyMMdd_HHmmss}00{counter}{make}{hdrStr}.jpg";
                var newPath = Path.Combine(Program.NEW, newFileName);
                if (File.Exists(newPath))
                {
                    Console.WriteLine($"exists: {f} -> {newPath}");
                    Histogram newHisto;
                    using (var img = Image.FromFile(newPath))
                    {
                        newHisto = new Histogram(img);
                    }
                    var equal = newHisto.Equals(oldHisto);
                    Console.WriteLine($"{newFileName} ?= {Path.GetFileName(f)}: {equal}");
                    if (equal)
                        File.Delete(f);
                }
                else
                {
                    Console.WriteLine("move: " + newFileName);
                    File.Move(f, newPath);
                }

                Console.WriteLine($"{Path.GetFileName(f)} -> {newFileName}");
            }

            Console.WriteLine("done");
        }

        static string getNullTermString(PropertyItem p)
        {
            string ret = Encoding.ASCII.GetString(p.Value);
            int nullNdx = ret.IndexOf('\0');
            if (nullNdx >= 0)
                ret = ret.Substring(0, nullNdx);
            return ret;
        }
    }

    static class DeleteDups
    {
        static readonly object sLock = new object();
        static readonly Dictionary<byte[], string> sOldHash = new Dictionary<byte[], string>(new ByteArrayComparer());
        static readonly Dictionary<byte[], string> sNewHash = new Dictionary<byte[], string>(new ByteArrayComparer());

        public static void DoDeleteDups()
        {
            var hashOld = startHashFiles(Program.OLD);
            var hashNew = startHashFiles(Program.NEW);

            var oldProcessor = createActionBlock<KeyValuePair<string, byte[]>>(processOld);
            var newProcessor = createActionBlock<KeyValuePair<string, byte[]>>(processNew);

            hashOld.LinkTo(oldProcessor, new DataflowLinkOptions()
            {
                PropagateCompletion = true
            });
            hashNew.LinkTo(newProcessor, new DataflowLinkOptions()
            {
                PropagateCompletion = true
            });

            Task.WaitAll(oldProcessor.Completion, newProcessor.Completion);

            Console.WriteLine("Done.");
        }

        private static TransformBlock<string, KeyValuePair<string, byte[]>> startHashFiles(string path)
        {
            var hashOld = new TransformBlock<string, KeyValuePair<string, byte[]>>(new Func<string, KeyValuePair<string, byte[]>>(doHash), new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount / 2
            });
            foreach (var fPath in Directory.GetFiles(path))
            {
                hashOld.Post(fPath);
            }
            hashOld.Complete();
            return hashOld;
        }

        private static ActionBlock<T> createActionBlock<T>(Action<T> a)
        {
            return new ActionBlock<T>(a, new ExecutionDataflowBlockOptions()
            {
                EnsureOrdered = false
            });
        }

        private static void processOld(KeyValuePair<string, byte[]> f)
        {
            lock (sLock)
            {
                if (sNewHash.ContainsKey(f.Value))
                {
                    Console.WriteLine("(old) would delete: " + f.Key);
                    File.Delete(f.Key);
                }
                else
                {
                    sOldHash.Add(f.Value, f.Key);
                }
            }
        }

        private static void processNew(KeyValuePair<string, byte[]> f)
        {
            lock (sLock)
            {
                string oldPath;
                if (sOldHash.TryGetValue(f.Value, out oldPath))
                {
                    Console.WriteLine("(new) would delete: " + oldPath);
                    File.Delete(oldPath);
                    sOldHash.Remove(f.Value);
                }
                else
                {
                    sNewHash[f.Value] = f.Key;
                }
            }
        }

        static KeyValuePair<string, byte[]> doHash(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var ret = new KeyValuePair<string, byte[]>(path, Hasher.ComputeHash(fs));
                return ret;
            }
        }

        [ThreadStatic]
        static HashAlgorithm tHasher;

        static HashAlgorithm Hasher
        {
            get
            {
                if (tHasher == null)
                {
                    return tHasher = SHA512.Create();
                }
                else
                {
                    return tHasher;
                }
            }
        }


        class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[] x, byte[] y)
            {
                if (object.ReferenceEquals(x, y))
                    return true;
                if (x == null || y == null)
                    return false;
                if (x.Length != y.Length)
                    return false;
                for (int i = 0; i < x.Length; i++)
                {
                    if (x[i] != y[i])
                        return false;
                }
                return true;
            }

            public int GetHashCode(byte[] obj)
            {
                if (obj == null)
                    return 0;
                int ret = 0;
                for (int i = 0; i < obj.Length; i++)
                {
                    ret = CombineHashCodes(ret, obj[i]);
                }
                return ret;
            }

            static int CombineHashCodes(int h1, int h2)
            {
                return (h1 << 5) + h1 ^ h2;
            }

        }
    }
}
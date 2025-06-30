using System;
using System.IO;
using System.IO.Compression;
using System.Text;
namespace SaveTheSize
{
    public class Swag
    {
        private const uint MAGIC_NUMBER = 0x53545346;
        private const uint VERSION = 1;
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        private struct Header
        {
            public uint magic;
            public uint version;
            public ulong originalSize;
            public ulong compressedSize;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 256)]
            public byte[] originalName;
            public Header(uint magic, uint version, ulong originalSize, ulong compressedSize, string filename)
            {
                this.magic = magic;
                this.version = version;
                this.originalSize = originalSize;
                this.compressedSize = compressedSize;
                this.originalName = new byte[256];
                byte[] nameBytes = Encoding.UTF8.GetBytes(filename);
                int copyLength = Math.Min(nameBytes.Length, 255);
                Array.Copy(nameBytes, this.originalName, copyLength);
            }
        }
        public static byte[] ReadFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"cannot open file: {filePath}");
            }
            try
            {
                return File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                throw new IOException($"error reading file: {filePath}", ex);
            }
        }
        public static void WriteFile(string filePath, byte[] data)
        {
            try
            {
                File.WriteAllBytes(filePath, data);
            }
            catch (Exception ex)
            {
                throw new IOException($"error writing file: {filePath}", ex);
            }
        }
        public static byte[] CompressData(byte[] input)
        {
            if (input == null || input.Length == 0)
            {
                return new byte[0];
            }
            using (var outputStream = new MemoryStream())
            {
                using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal))
                {
                    deflateStream.Write(input, 0, input.Length);
                }
                return outputStream.ToArray();
            }
        }
        public static byte[] DecompressData(byte[] compressed, ulong originalSize)
        {
            if (compressed == null || compressed.Length == 0)
            {
                return new byte[0];
            }
            using (var inputStream = new MemoryStream(compressed))
            using (var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                deflateStream.CopyTo(outputStream);
                byte[] result = outputStream.ToArray();
                if ((ulong)result.Length != originalSize)
                {
                    throw new InvalidDataException("decompressed size mismatch");
                }
                return result;
            }
        }
        private static byte[] StructToBytes<T>(T structure) where T : struct
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf(structure);
            byte[] bytes = new byte[size];
            IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(structure, ptr, false);
                System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, size);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
            }
            return bytes;
        }
        private static T BytesToStruct<T>(byte[] bytes) where T : struct
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
            if (bytes.Length < size)
            {
                throw new ArgumentException("byte array is too small for the structure");
            }
            IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, size);
                return (T)System.Runtime.InteropServices.Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
            }
        }
        public static void CompressFile(string inputFile, string outputFile = "")
        {
            byte[] inputData = ReadFile(inputFile);
            byte[] compressedData = CompressData(inputData);
            string filename = Path.GetFileName(inputFile);
            Header header = new Header(MAGIC_NUMBER, VERSION, (ulong)inputData.Length, (ulong)compressedData.Length, filename);
            string outFile = string.IsNullOrEmpty(outputFile) ? inputFile + ".savethesize" : outputFile;
            byte[] headerBytes = StructToBytes(header);
            byte[] output = new byte[headerBytes.Length + compressedData.Length];
            Array.Copy(headerBytes, 0, output, 0, headerBytes.Length);
            Array.Copy(compressedData, 0, output, headerBytes.Length, compressedData.Length);
            WriteFile(outFile, output);
            double compressionRatio = (double)compressedData.Length / inputData.Length * 100.0;
            Console.WriteLine("file compressed successfully!!");
            Console.WriteLine($"input:  {inputFile} ({inputData.Length} bytes)");
            Console.WriteLine($"output: {outFile} ({output.Length} bytes)");
            Console.WriteLine($"compression ratio: {compressionRatio:F1}%");
        }
        public static void DecompressFile(string inputFile, string outputFile = "")
        {
            byte[] inputData = ReadFile(inputFile);
            int headerSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Header));
            if (inputData.Length < headerSize)
            {
                throw new InvalidDataException("invalid compressed file: too small");
            }
            Header header = BytesToStruct<Header>(inputData);
            if (header.magic != MAGIC_NUMBER)
            {
                throw new InvalidDataException("invalid compressed file: wrong magic number");
            }
            if (header.version != VERSION)
            {
                throw new InvalidDataException("unsupported file version");
            }
            if ((ulong)inputData.Length != (ulong)headerSize + header.compressedSize)
            {
                throw new InvalidDataException("invalid compressed file: size mismatch");
            }
            byte[] compressedData = new byte[header.compressedSize];
            Array.Copy(inputData, headerSize, compressedData, 0, (int)header.compressedSize);
            byte[] decompressedData = DecompressData(compressedData, header.originalSize);
            string outFile;
            if (!string.IsNullOrEmpty(outputFile))
            {
                outFile = outputFile;
            }
            else
            {
                string originalName = Encoding.UTF8.GetString(header.originalName).TrimEnd('\0');
                outFile = originalName;
                if (File.Exists(outFile))
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(outFile);
                    string extension = Path.GetExtension(outFile);
                    outFile = nameWithoutExt + "_decompressed" + extension;
                }
            }
            WriteFile(outFile, decompressedData);
            Console.WriteLine("file decompressed successfully!");
            Console.WriteLine($"input:  {inputFile} ({inputData.Length} bytes)");
            Console.WriteLine($"output: {outFile} ({decompressedData.Length} bytes)");
            Console.WriteLine($"original filename: {Encoding.UTF8.GetString(header.originalName).TrimEnd('\0')}");
        }
        public static void ShowUsage(string programName)
        {
            Console.WriteLine("save the size");
            Console.WriteLine("usage:");
            Console.WriteLine($"  compress:   {programName} -c <input_file> [output_file]");
            Console.WriteLine($"  decompress: {programName} -d <compressed_file> [output_file]");
            Console.WriteLine($"  info:       {programName} -i <compressed_file>");
            Console.WriteLine();
            Console.WriteLine("options:");
            Console.WriteLine("  -c          compress mode");
            Console.WriteLine("  -d          decompress mode");
            Console.WriteLine("  -i          show file info");
            Console.WriteLine("  -h, --help  show this help message");
        }
        public static void ShowFileInfo(string inputFile)
        {
            byte[] inputData = ReadFile(inputFile);
            int headerSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Header));
            if (inputData.Length < headerSize)
            {
                throw new InvalidDataException("Invalid compressed file: too small");
            }
            Header header = BytesToStruct<Header>(inputData);
            if (header.magic != MAGIC_NUMBER)
            {
                throw new InvalidDataException("Not a valid compressed file");
            }
            Console.WriteLine("compressed File Information:");
            Console.WriteLine($"file: {inputFile}");
            Console.WriteLine($"original name: {Encoding.UTF8.GetString(header.originalName).TrimEnd('\0')}");
            Console.WriteLine($"original size: {header.originalSize} bytes");
            Console.WriteLine($"compressed size: {header.compressedSize} bytes");
            Console.WriteLine($"file size: {inputData.Length} bytes");
            Console.WriteLine($"version: {header.version}");
            double compressionRatio = (double)header.compressedSize / header.originalSize * 100.0;
            Console.WriteLine($"compression ratio: {compressionRatio:F1}%");
        }
    }
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    Swag.ShowUsage(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name);
                    return 1;
                }
                string mode = args[0];
                if (mode == "-h" || mode == "--help")
                {
                    Swag.ShowUsage(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name);
                    return 0;
                }
                if (mode == "-c")
                {
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("error: input file required for compression");
                        return 1;
                    }
                    string inputFile = args[1];
                    string outputFile = args.Length > 2 ? args[2] : "";
                    if (!File.Exists(inputFile))
                    {
                        Console.Error.WriteLine($"error: input file does not exist: {inputFile}");
                        return 1;
                    }
                    Swag.CompressFile(inputFile, outputFile);
                }
                else if (mode == "-d")
                {
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("error: input file required for decompression");
                        return 1;
                    }
                    string inputFile = args[1];
                    string outputFile = args.Length > 2 ? args[2] : "";
                    if (!File.Exists(inputFile))
                    {
                        Console.Error.WriteLine($"error: input file does not exist: {inputFile}");
                        return 1;
                    }
                    Swag.DecompressFile(inputFile, outputFile);
                }
                else if (mode == "-i")
                {
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("error: input file required for info");
                        return 1;
                    }
                    string inputFile = args[1];
                    if (!File.Exists(inputFile))
                    {
                        Console.Error.WriteLine($"error: input file does not exist: {inputFile}");
                        return 1;
                    }
                    Swag.ShowFileInfo(inputFile);
                }
                else
                {
                    Console.Error.WriteLine($"error: unknown mode '{mode}'");
                    Swag.ShowUsage(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name);
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 1;
            }
            return 0;
        }
    }
}

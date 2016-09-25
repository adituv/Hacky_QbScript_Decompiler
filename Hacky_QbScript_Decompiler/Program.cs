using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hacky_QbScript_Decompiler {
    using System.Linq;
    using System.Reflection;

    class Program {
        static Dictionary<byte, Func<BinaryReader,string>> translations = new Dictionary<byte, Func<BinaryReader, string>>()
        {
            { 0x01, (b) => "\r\n" },
            { 0x03, (b) => "map {" },
            { 0x04, (b) => "}" },
            { 0x05, (b) => "[" },
            { 0x06, (b) => "]" },
            { 0x07, (b) => "=" },
            { 0x08, (b) => "." },
            { 0x0A, (b) => "-" },
            { 0x0B, (b) => "+" },
            { 0x0C, (b) => "*" },
            { 0x0D, (b) => "/" },
            { 0x0E, (b) => "(" },
            { 0x0F, (b) => ")" },
            { 0x12, (b) => "<" },
            { 0x13, (b) => "<=" },
            { 0x14, (b) => ">" },
            { 0x15, (b) => ">=" },
            { 0x16, (b) => {
                var k = b.ReadUInt32();
                if (debugNames.ContainsKey(k)) return debugNames[k][0];
                else return "$" + k.ToString("X8");}
            },
            { 0x17, (b) => b.ReadInt32().ToString() },
            { 0x1A, (b) => { var bs = b.ReadBytes(4); var v = BitConverter.ToSingle(bs,0); return v.ToString(); } },
            { 0x1B, (b) => { var len = (int) b.ReadUInt32(); var bs = b.ReadBytes(len); return Encoding.ASCII.GetChars(bs).ToString(); } },
            { 0x1E, (b) => { var bs = b.ReadBytes(12); var v1 = BitConverter.ToSingle(bs,0); var v2 = BitConverter.ToSingle(bs,4);
                             var v3 = BitConverter.ToSingle(bs,8); return string.Format("({0:R}, {1:R}, {2:R})", v1, v2, v3); } },
            { 0x1F, (b) => { var bs = b.ReadBytes(8); var v1 = BitConverter.ToSingle(bs,0); var v2 = BitConverter.ToSingle(bs,4);
                             return string.Format("({0:R}, {1:R})", v1, v2); } },
            { 0x20, (b) => "while" },
            { 0x21, (b) => "wend" },
            { 0x22, (b) => "break" },
            { 0x24, (b) => "\n[END OF SCRIPT]" },
            { 0x25, (b) => "if" },
            { 0x26, (b) => "else" },
            { 0x27, (b) => "elif" },
            { 0x28, (b) => "endif" },
            { 0x29, (b) => "return" },
            { 0x2C, (b) => "{PASSTHROUGH}" },
            { 0x2D, (b) => "local" },
            { 0x39, (b) => "!" },
            { 0x3A, (b) => "&" },
            { 0x3B, (b) => "|" },
            { 0x3C, (b) => "select case" },
            { 0x3D, (b) => "end select" },
            { 0x3E, (b) => "case" },
            { 0x3F, (b) => "default" },
            { 0x42, (b) => ":" },
            { 0x47, (b) => { b.ReadBytes(2); return "if"; } },
            { 0x48, (b) => { b.ReadBytes(2); return "else"; } },
            { 0x49, (b) => { b.ReadBytes(2); return "break"; } },
            { 0x4B, (b) => "*" },
            { 0x4C, (b) => { var len = (int) b.ReadUInt32(); var bs = b.ReadBytes(len); return Encoding.BigEndianUnicode.GetChars(bs).ToString(); } },
            { 0x4A, translateQbStruct }
        };

        private static Dictionary<uint, string[]> debugNames; 

        static string translateQbStruct(BinaryReader br) {
            StringBuilder sb;
            var len = br.ReadUInt16();
            byte b;
            do {
                b = br.ReadByte();
            } while (b == 0);
            if(br.ReadByte() != 1 || br.ReadByte() != 0) {
                throw new Exception("Invalid Qb struct");
            }
            // Temp: skip qb struct data
            br.ReadBytes(len - 4);
            return "(Qb Struct)";
        }

        static Dictionary<uint, string[]> LoadDebugItems() {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream("Hacky_QbScript_Decompiler.dbg_extra.pak.xen");
            if (resourceStream == null) throw new NullReferenceException("Failed to load the debug names");

            var tempResult = new Dictionary<uint, List<string>>();
            var result = new Dictionary<uint, string[]>();

            using (var sr = new StreamReader(resourceStream)) {
                while (!sr.EndOfStream) {
                    string[] parts = sr.ReadLine().Split(' ');
                    if (parts.Length <= 1) {
                        continue;
                    }

                    uint key;

                    try {
                        // tryparse is messy because of stripping the hex specifier
                        key = Convert.ToUInt32(parts[0], 16);
                    } catch {
                        continue;
                    }

                    var value = string.Join(" ", parts.Skip(1)).ToLowerInvariant();
                    if (!tempResult.ContainsKey(key)) {
                        tempResult.Add(key, new List<string>());
                    }
                    tempResult[key].Add(value);
                }

                //Get only distinct values.  Differences occur generally when a key is used for a file path as well
                //as the file name even when the checksum is not actually that of the file path.
                foreach (var kv in tempResult) {
                    kv.Value.Sort();
                    result.Add(kv.Key, kv.Value.Distinct().ToArray());
                }

                return result;
            }
        }


        static int Main(string[] args) {
            if(args.Length < 2) {
                Console.WriteLine("Usage: {0} inputfile outputfile", Process.GetCurrentProcess().ProcessName);
                return 1;
            }

            try
            {
                debugNames = LoadDebugItems();
                using (var inf = File.OpenRead(args[0]))
                using (var of = File.OpenWrite(args[1]))
                using (var inb = new BinaryReader(inf))
                using (var sw = new StreamWriter(of)) {
                    while(inb.PeekChar() != -1) { // !eof
                        byte b = inb.ReadByte();
                        Func<BinaryReader,string> f;
                        if(translations.TryGetValue(b, out f)) {
                            sw.Write(f(inb));
                            sw.Write(" ");
                        } else {
                            sw.WriteLine();
                            sw.WriteLine("Unknown opcode: " + b);
                        }
                    }
                }     
            } catch(Exception ex) {
                Console.WriteLine("Error occurred: {0}", ex.Message);
                return 1;
            }

            return 0;
        }
    }
}

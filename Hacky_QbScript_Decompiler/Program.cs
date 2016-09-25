using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hacky_QbScript_Decompiler {
    using System.Linq;
    using System.Reflection;

    class Program {
        #region opcode translations
        static Dictionary<byte, Func<BinaryReader,string>> translations = new Dictionary<byte, Func<BinaryReader, string>>
        {
            { 0x01, (b) => "\r\n" },
            { 0x03, (b) => "(map){" },
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
            { 0x1B, (b) => { var len = (int) b.ReadUInt32(); var bs = b.ReadBytes(len); return "\"" + new string(Encoding.ASCII.GetChars(bs)) + "\""; } },
            { 0x1E, (b) => { var v1 = b.ReadSingle(); var v2 = b.ReadSingle(); var v3 = b.ReadSingle();
                             return string.Format("({0:R}, {1:R}, {2:R})", v1, v2, v3); } },
            { 0x1F, (b) => { var v1 = b.ReadSingle(); var v2 = b.ReadSingle();
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
            { 0x4C, (b) => { var len = (int) b.ReadUInt32(); var bs = b.ReadBytes(len); return "L\"" + new string(Encoding.BigEndianUnicode.GetChars(bs)) + "\"";} },
            { 0x4A, translateQbStruct }
        };
        #endregion
        #region qbstructitem parsing

        private static Dictionary<uint, Func<BinaryReader, uint, string>> structItemParsers = new Dictionary<uint, Func<BinaryReader, uint, string>>
        {
            { 0x8100, (br,pos) => {
                var data = br.ReadBytes(4);
                var n = BitConverter.ToInt32(data, 0);
                return n.ToString();
            } },
            { 0x8200, (br,pos) => {
                var data = br.ReadBytes(4);
                var n = BitConverter.ToSingle(data, 0);
                return n.ToString("R");
            } },
            { 0x8300, (br, pos) => {
                var data = br.ReadBytes(8);
                var stringStart = BitConverter.ToUInt32(data, 0);
                var stringEnd = BitConverter.ToUInt32(data, 4);

                if (stringStart < (pos + 8))
                {
                    throw new Exception("Data section occurs before header in Qb struct!");
                }
                else if (stringStart > pos + 8)
                {
                    br.ReadBytes((int)(stringStart - pos - 8));
                }

                var strData = br.ReadBytes((int)(stringEnd - stringStart));

                return Encoding.ASCII.GetChars(strData).ToString();
            } },
        };
        #endregion

        private static Dictionary<uint, string[]> debugNames;

        static byte[] swapEndianness(byte[] data)
        {
            return data.Reverse().ToArray();
        }

        static string translateQbStruct(BinaryReader br)
        {
            var sb = new StringBuilder();
            var len = br.ReadUInt16();
            byte b;
            sb.AppendLine("(struct){");
            do {
                b = br.ReadByte();
            } while (b == 0);
            if(b != 1 || br.ReadByte() != 0) {
                throw new Exception("Invalid Qb struct");
            }
            br.ReadBytes(len - 4);
            /*
            uint nextPos = br.ReadUInt32();
            uint curPos = 8;
            uint type, crc;
            while (nextPos != 0)
            {
                if (curPos < nextPos)
                {
                    br.ReadBytes((int)(nextPos - curPos));
                    curPos = nextPos;
                }

                uint dataStartPos;
                byte[] data;
                string valRep;
                type = br.ReadUInt32();
                crc = BitConverter.ToUInt32(swapEndianness(br.ReadBytes(4)), 0);
                data = swapEndianness(br.ReadBytes(4));

                switch (type)
                {
                    case 0x8100:
                        valRep = BitConverter.ToInt32(data, 0).ToString();
                        nextPos = BitConverter.ToUInt32(swapEndianness(br.ReadBytes(4)), 0);
                        curPos += 16;
                        break;
                    case 0x8200:
                        valRep = BitConverter.ToSingle(data, 0).ToString("R");
                        nextPos = BitConverter.ToUInt32(swapEndianness(br.ReadBytes(4)), 0);
                        curPos += 16;
                        break;
                    case 0x8300:
                        curPos += 8;
                        dataStartPos = BitConverter.ToUInt32(data, 0);
                        nextPos = BitConverter.ToUInt32(swapEndianness(br.ReadBytes(4)), 0);
                        if (dataStartPos > curPos)
                        {
                            br.ReadBytes((int)(dataStartPos - curPos));
                            // curPos = dataStartPos;
                        }
                        data = br.ReadBytes((int)(nextPos - dataStartPos));
                        curPos = nextPos;
                        valRep = Encoding.ASCII.GetChars(data).ToString();
                        break;
                    case 0x8400:
                        curPos += 8;
                        dataStartPos = BitConverter.ToUInt32(data, 0);
                        nextPos = BitConverter.ToUInt32(swapEndianness(br.ReadBytes(4)), 0);
                        if (dataStartPos > curPos) {
                            br.ReadBytes((int) (dataStartPos - curPos));
                            // curPos = dataStartPos;
                        }
                        data = br.ReadBytes((int) (nextPos - dataStartPos));
                        curPos = nextPos;
                        valRep = Encoding.BigEndianUnicode.GetChars(data).ToString();
                        break;
                    case 0x8500:
                        curPos += 8;
                        dataStartPos = BitConverter.ToUInt32(data, 0);
                        nextPos = BitConverter.ToUInt32(swapEndianness(br.ReadBytes(4)), 0);
                        if (dataStartPos > curPos) {
                            br.ReadBytes((int) (dataStartPos - curPos));
                            curPos = dataStartPos;
                        }
                        if (br.ReadUInt32() != 0x100) throw new Exception("Expecting float array start; not found.");
                        data = br.ReadBytes(4);


                }
            }*/
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

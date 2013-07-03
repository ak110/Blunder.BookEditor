using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Diagnostics;

namespace ShogiCore.Book {
    /// <summary>
    /// 定跡DB
    /// </summary>
    public class BookDB : IDisposable {
        /// <summary>
        /// logger
        /// </summary>
        static readonly log4net.ILog logger = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const int IndexCount = 0x4000;
        public const int IndexMask = 0x3fff;

        public const int IndexEntrySize = sizeof(uint) + sizeof(ushort);
        public const int BookEntrySize = sizeof(ulong) + sizeof(ushort) + sizeof(short);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        public struct IndexEntry {
            public uint Offset;
            public ushort Size;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        public struct BookEntry {
            public ulong Hash;
            public ushort Move;
            public short Value; // 得点
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        public struct BookEntryForWrite {
            public ulong Hash;
            public ushort Move;
            public ulong Value; // 得点
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        public struct BookEntryForRead { // 読むときはハッシュ要らないので。
            public ushort Move;
            public short Value; // 得点
        }

        /// <summary>
        /// 定跡手データ
        /// </summary>
        public struct BookMove {
            /// <summary>
            /// 指し手
            /// </summary>
            public Move Move;
            /// <summary>
            /// 得点
            /// </summary>
            public short Value;
        }

        object syncObject = new object();
        BinaryReader reader = null;

        /// <summary>
        /// ファイル名
        /// </summary>
        public string FileName { get; private set; }

        /// <summary>
        /// 初期化。
        /// </summary>
        public BookDB(string fileName) {
            FileName = fileName;
        }

        /// <summary>
        /// ファイルの変更
        /// </summary>
        public void ChangeFile(string fileName) {
            lock (syncObject) {
                if (FileName != fileName) {
                    Dispose();
                    FileName = fileName;
                }
            }
        }

        /// <summary>
        /// 後始末
        /// </summary>
        public void Dispose() {
            lock (syncObject) {
                if (reader != null) {
                    reader.Close();
                    reader = null;
                }
            }
        }

        /// <summary>
        /// ファイルが無かったり空だったりしたらfalse。読み込み可能と思われるならtrue。
        /// </summary>
        public bool IsReady() {
            lock (syncObject) {
                return reader != null ||
                    (File.Exists(FileName) &&
                    IndexEntrySize * IndexCount < new FileInfo(FileName).Length);
            }
        }

        /// <summary>
        /// 書き込み
        /// </summary>
        public void WriteAllEntries(Dictionary<int, List<BookEntryForWrite>> writeBuffer) {
            checked { // 色々少ないバイト数に詰め込むので念のため。。
                int offset = IndexEntrySize * IndexCount;
                using (FileStream stream = File.Create(FileName, 0x1000, FileOptions.RandomAccess))
                using (BinaryWriter writer = new BinaryWriter(stream)) {
                    foreach (var item in writeBuffer) {
                        int entriesSize = item.Value.Count * BookEntrySize;
                        // index
                        if (item.Key < 0 || IndexCount <= item.Key) {
                            throw new ArgumentException("キーの値が不正です", "writeBuffer");
                        }
                        writer.Seek(item.Key * IndexEntrySize, SeekOrigin.Begin);
                        writer.Write((uint)offset);
                        writer.Write((ushort)entriesSize);
                        // book
                        item.Value.Sort((x, y) => (int)y.Value - (int)x.Value); // Freqで降順ソートしとく。Hashでもソートした方がいいかもしらんが…。
                        writer.Seek(offset, SeekOrigin.Begin);
                        int shift = GetShiftCount(item.Value);
                        foreach (var bookEntry in item.Value) {
                            writer.Write(bookEntry.Hash);
                            writer.Write(bookEntry.Move);
                            writer.Write((short)Math.Max(bookEntry.Value >> shift, 0));
                        }
                        offset += entriesSize;
                    }
                }
            }
        }

        /// <summary>
        /// 必要なシフト量の算出
        /// </summary>
        private static int GetShiftCount(List<BookEntryForWrite> list) {
            int shift = 0;
            while (list.Exists(x => (ulong)short.MaxValue < (x.Value >> shift))) { // 手抜き実装
                shift++;
            }
            return shift;
        }

        /// <summary>
        /// ファイルを読み込みオープンしておく。
        /// </summary>
        public void PrepareRead() {
            if (reader == null) {
                reader = new BinaryReader(File.OpenRead(FileName));
                // index部をダミー読み込みしてみる
                reader.ReadBytes(0x10000); // 85000バイト以上だとLarge Object Heapに入ってしまうので適当に。
            }
        }

        /// <summary>
        /// 全エントリの読み込み
        /// </summary>
        public void ReadAllEntries(Dictionary<int, List<BookEntryForWrite>> readBuffer) {
            lock (syncObject) {
                long fileSize = reader.BaseStream.Length;
                for (int key = 0, indexOffset = 0; key < IndexCount; key++, indexOffset += IndexEntrySize) {
                    reader.BaseStream.Seek(indexOffset, SeekOrigin.Begin);
                    // index
                    uint offset = reader.ReadUInt32();
                    int size = reader.ReadUInt16();
                    int count = size / BookEntrySize;
                    // entry
                    if (0 < count && offset + size <= fileSize) {
                        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                        byte[] buffer = new byte[size];
                        reader.Read(buffer, 0, buffer.Length);
                        unsafe {
                            List<BookEntryForWrite> list;
                            if (!readBuffer.TryGetValue(key, out list)) {
                                readBuffer[key] = list = new List<BookEntryForWrite>();
                            }
                            fixed (byte* p = buffer) {
                                BookEntry* be = (BookEntry*)p;
                                for (int i = 0; i < count; i++) {
                                    list.Add(new BookEntryForWrite
                                    {
                                        Hash = be[i].Hash,
                                        Move = be[i].Move,
                                        Value = (uint)be[i].Value, // TODO: この辺書き込み側も合わせて可逆っぽい変換の方法考えた方がいいかも…。
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        public struct CountData {
            public int AllMoves;
            public int AllPhases;
        }
        /// <summary>
        /// 個数だけの取得
        /// </summary>
        /// <returns>局面数、手数</returns>
        public CountData GetCount() {
            int allMoves = 0;
            HashSet<ulong> phaseList = new HashSet<ulong>();
            lock (syncObject) {
                if (reader == null) {
                    reader = new BinaryReader(File.OpenRead(FileName));
                }
                long fileSize = reader.BaseStream.Length;
                for (int key = 0, indexOffset = 0; key < IndexCount; key++, indexOffset += IndexEntrySize) {
                    reader.BaseStream.Seek(indexOffset, SeekOrigin.Begin);
                    // index
                    uint offset = reader.ReadUInt32();
                    int size = reader.ReadUInt16();
                    int count = size / BookEntrySize;
                    allMoves += count; // 手数
                    // entry
                    if (0 < count && offset + size <= fileSize) {
                        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                        byte[] buffer = new byte[size];
                        reader.Read(buffer, 0, buffer.Length);
                        unsafe {
                            fixed (byte* p = buffer) {
                                BookEntry* be = (BookEntry*)p;
                                for (int i = 0; i < count; i++) {
                                    phaseList.Add(be[i].Hash);
                                }
                            }
                        }
                    }
                }
            }
            int allPhases = phaseList.Count;
            return new CountData { AllMoves = allMoves, AllPhases = allPhases };
        }

        /// <summary>
        /// 定跡DBからのデータの読み出し。
        /// </summary>
        public List<BookEntryForRead> ReadBookEntries(ulong hash) {
            lock (syncObject) {
                if (reader == null) {
                    reader = new BinaryReader(File.OpenRead(FileName));
                }

                // index
                reader.BaseStream.Seek((long)(hash & IndexMask) * IndexEntrySize, SeekOrigin.Begin);
                IndexEntry indexEntry;
                indexEntry.Offset = reader.ReadUInt32();
                indexEntry.Size = reader.ReadUInt16();

                int count = indexEntry.Size / BookEntrySize;
                List<BookEntryForRead> resultList = new List<BookEntryForRead>();

                // entry
                reader.BaseStream.Seek(indexEntry.Offset, SeekOrigin.Begin);
                byte[] buffer = new byte[indexEntry.Size];
                reader.Read(buffer, 0, buffer.Length);
                unsafe {
                    fixed (byte* p = buffer) {
                        BookEntry* be = (BookEntry*)p;
                        for (int i = 0; i < count; i++) {
                            if (be[i].Hash != hash) continue;
                            resultList.Add(new BookEntryForRead
                            {
                                Move = be[i].Move,
                                Value = be[i].Value,
                            });
                        }
                    }
                }

                return resultList;
            }
        }

        /// <summary>
        /// 読み込みその2
        /// </summary>
        public List<BookMove> GetBookMoves(Board board) {
            List<BookEntryForRead> resultList = ReadBookEntries(board.FullHashValue);

            List<BookMove> moves = new List<BookMove>();
            foreach (var item in resultList) {
                Move move = Move.FromBinary(board, item.Move);
                if (board.IsLegalMove(ref move)) {
                    moves.Add(new BookMove { Move = move, Value = item.Value });
                } else {
                    logger.Warn("定跡DBに違法な手？: " + move.ToString(board) + ", " + board.GetIllegalReason(move));
                }
            }

            Debug.Assert(moves.TrueForAll(x => 0 < x.Value), "定跡手のMove.Valueが0以下?");
            return moves;
        }
    }
}

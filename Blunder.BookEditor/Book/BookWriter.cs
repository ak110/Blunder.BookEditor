using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace ShogiCore.Book {
    /// <summary>
    /// 定跡DBの構築を行う為のクラス
    /// </summary>
    public class BookWriter : IDisposable {
        Dictionary<int, List<BookDB.BookEntryForWrite>> writeBuffer
            = new Dictionary<int, List<BookDB.BookEntryForWrite>>();

        /// <summary>
        /// BookDB
        /// </summary>
        public BookDB BookDB { get; private set; }

        /// <summary>
        /// 新規作成
        /// </summary>
        public static BookWriter Create(string fileName) {
            return new BookWriter(fileName, true);
        }
        /// <summary>
        /// 開く
        /// </summary>
        public static BookWriter Open(string fileName) {
            return new BookWriter(fileName, false);
        }

        /// <summary>
        /// 初期化。
        /// </summary>
        public BookWriter(string fileName, bool create)
            : this(new BookDB(fileName), create) {}

        /// <summary>
        /// 初期化。
        /// </summary>
        public BookWriter(BookDB bookDB, bool create) {
            BookDB = bookDB;
            if (!create) {
                // open時は既存のがあれば読み込む。
                if (BookDB.IsReady()) {
                    BookDB.ReadAllEntries(writeBuffer);
                }
            }
        }

        /// <summary>
        /// 後始末。
        /// </summary>
        public void Dispose() {
            if (BookDB != null) {
                if (0 < writeBuffer.Count) Flush();
                BookDB.Dispose();
                BookDB = null;
            }
        }

        /// <summary>
        /// 指し手を含んでいたらtrue
        /// </summary>
        /// <param name="fullHashValue">持ち駒を含むハッシュ値</param>
        /// <param name="move">指し手</param>
        /// <returns>含んでいるかどうか</returns>
        public bool Contains(ulong fullHashValue, Move move) {
            List<BookDB.BookEntryForWrite> list;
            if (writeBuffer.TryGetValue((int)(fullHashValue & BookDB.IndexMask), out list)) {
                ushort moveValue = move.ToBinary();
                if (list.Exists(x => x.Hash == fullHashValue && x.Move == moveValue)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 棋譜の登録
        /// </summary>
        /// <param name="notation">棋譜</param>
        /// <param name="moveCount">登録する手数</param>
        /// <param name="addTurn">登録する手番。-1で両方。</param>
        /// <param name="value">加算する得点</param>
        public void AddNotation(ShogiCore.Notation.Notation notation,
            int moveCount, int addTurn, ulong value) {
            if (addTurn < -1 || 1 < addTurn) {
                throw new ArgumentOutOfRangeException("ignoreTurn");
            }
            int ignoreTurn =
                addTurn == 0 ? 1 :
                addTurn == 1 ? 0 : -1;

            Board board = new Board(notation.InitialBoard);
            int i = 0;
            foreach (Move move in board.ReadNotation(notation.Moves)) {
                // 登録
                if (board.Turn != ignoreTurn) {
                    Add(board.FullHashValue, move, value);
                }
                if (moveCount <= ++i) break;
            }
        }

        /// <summary>
        /// 指し手の登録
        /// </summary>
        /// <param name="fullHashValue">持ち駒を含むハッシュ値</param>
        /// <param name="move">指し手</param>
        /// <param name="value">加算する得点</param>
        public void Add(ulong fullHashValue, Move move, ulong value) {
            int writeBufferKey = (int)(fullHashValue & BookDB.IndexMask);
            List<BookDB.BookEntryForWrite> list;
            if (!writeBuffer.TryGetValue(writeBufferKey, out list)) {
                writeBuffer[writeBufferKey] = list = new List<BookDB.BookEntryForWrite>();
            }

            ushort moveValue = move.ToBinary();
            int index = list.FindIndex(x => x.Hash == fullHashValue && x.Move == moveValue);
            if (0 <= index) {
                // 既にあればvalueを加算
                var entry = list[index];
                entry.Value += value;
                list[index] = entry;
            } else {
                // 無ければ追加
                list.Add(new BookDB.BookEntryForWrite {
                    Hash = fullHashValue,
                    Move = moveValue,
                    Value = value,
                });
            }
        }

        /// <summary>
        /// 該当するものを全て削除
        /// </summary>
        /// <param name="pred">条件関数</param>
        public void RemoveAll(Predicate<BookDB.BookEntryForWrite> pred) {
            foreach (var list in writeBuffer.Values) {
                list.RemoveAll(pred);
            }
        }

        /// <summary>
        /// 全て変換
        /// </summary>
        /// <param name="conv">変換関数</param>
        public void ConvertAll(Converter<BookDB.BookEntryForWrite, BookDB.BookEntryForWrite> conv) {
            foreach (var list in writeBuffer.Values) {
                for (int i = 0, n = list.Count; i < n; i++) {
                    list[i] = conv(list[i]);
                }
            }
        }

        /// <summary>
        /// 書き込み
        /// </summary>
        public void Flush() {
            BookDB.WriteAllEntries(writeBuffer);
            writeBuffer.Clear();
        }

        /// <summary>
        /// 複製の作成
        /// </summary>
        public BookWriter CloneCreate(string fileName) {
            BookWriter copy = new BookWriter(fileName, true);
            foreach (var p in writeBuffer) {
                copy.writeBuffer.Add(p.Key, Utility.Clone(p.Value));
            }
            return copy;
        }

        /// <summary>
        /// 複製の作成
        /// </summary>
        public BookWriter CloneOpen(string fileName) {
            BookWriter copy = new BookWriter(fileName, false);
            foreach (var p in writeBuffer) {
                copy.writeBuffer.Add(p.Key, Utility.Clone(p.Value));
            }
            return copy;
        }
    }
}

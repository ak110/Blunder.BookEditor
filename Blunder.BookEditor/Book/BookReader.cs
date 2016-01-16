using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;

namespace ShogiCore.Book {
    /// <summary>
    /// BookDBなイベント
    /// </summary>
    public class BookReaderEventArgs : EventArgs {
        /// <summary>
        /// 盤面
        /// </summary>
        public Board Board { get; set; }
        /// <summary>
        /// 棋譜の手。
        /// </summary>
        public List<BookDB.BookMove> BookMoves { get; set; }
    }

    /// <summary>
    /// 定跡DBを参照して指し手を取得する処理
    /// </summary>
    public class BookReader : IDisposable {
        /// <summary>
        /// 定跡の選択方法
        /// </summary>
        public enum SelectionModes {
            NoBook, // 使わない
            Narrow, // 狭め
            Normal, // 通常
            Wide,   // 広め
            Random, // 候補手の中から完全にランダム (最も広い)
        }

        /// <summary>
        /// 定跡の選択方法。既定値はNormal。
        /// </summary>
        public SelectionModes SelectionMode { get; set; }
        /// <summary>
        /// この手数を超えたら参照しない。0なら無効(常に参照する)。負なら常に参照しない。
        /// 既定値は40
        /// </summary>
        public int UntilMove { get; set; }

        #region 前回のTryGetValueに関する情報など

        /// <summary>
        /// 前回の指し手。
        /// </summary>
        public BookDB.BookMove LastSelectedMove { get; set; }
        /// <summary>
        /// 該当棋譜数
        /// </summary>
        public int LastHitCount { get; set; }

        #endregion

        /// <summary>
        /// 棋譜の手を発見したぞイベント。
        /// </summary>
        public event EventHandler<BookReaderEventArgs> FoundBookMoves;

        /// <summary>
        /// BookDB
        /// </summary>
        public BookDB BookDB { get; private set; }

        BookReaderEventArgs bookReaderEventArgs = new BookReaderEventArgs(); // new避け。

        /// <summary>
        /// 初期化。
        /// </summary>
        public BookReader(string fileName)
            : this(new BookDB(fileName)) { }

        /// <summary>
        /// 初期化。
        /// </summary>
        public BookReader(BookDB bookDB) {
            BookDB = bookDB;
            SelectionMode = SelectionModes.Normal;
            UntilMove = 40;
        }

        /// <summary>
        /// 後始末
        /// </summary>
        public void Dispose() {
            if (BookDB != null) {
                BookDB.Dispose();
                BookDB = null;
            }
        }

        /// <summary>
        /// 初手で時間かからないように準備しておく。
        /// </summary>
        public void Prepare() {
            if (SelectionMode != SelectionModes.NoBook) {
                BookDB.PrepareRead();
            }
        }

        /// <summary>
        /// 手の取得
        /// </summary>
        /// <param name="board">盤面</param>
        /// <param name="move">手</param>
        /// <returns>取得出来なければfalse</returns>
        public bool TryGetMove(Board board, out BookDB.BookMove move) {
            // 使わない設定だったり、手数が超えてたり、ファイルが無かったり空だったりしたらfalse。
            if (SelectionMode == SelectionModes.NoBook ||
                (UntilMove != 0 && UntilMove < board.MoveCount) ||
                !BookDB.IsReady()) {
                LastSelectedMove = move = new BookDB.BookMove();
                return false;
            }

            List<BookDB.BookMove> resultList = GetBookMoves(board);

            if (0 < resultList.Count) {
                // 手の選択
                if (SelectionMode == SelectionModes.Random) {
                    // 完全にフラットなランダム
                    LastSelectedMove = move = resultList[RandUtility.Next(resultList.Count)];
                    return true;
                } else {
                    int sum;
                    if (SelectionMode == SelectionModes.Wide) { // SQRTる
                        for (int i = 0, n = resultList.Count; i < n; i++) {
                            var tmp = resultList[i];
                            tmp.Value = (short)Math.Max(Math.Sqrt(tmp.Value), 1);
                            resultList[i] = tmp;
                        }
                        sum = resultList.Sum(x => (int)x.Value);
                    } else {
                        sum = LastHitCount;
                    }

                    int r = SelectionMode == SelectionModes.Narrow ?
                        RandUtility.Next((sum + 1) / 2) : // ←後半は採用しない。
                        RandUtility.Next(sum);
                    foreach (var item in resultList) {
                        if (r < item.Value) {
                            LastSelectedMove = move = item;
                            return true;
                        }
                        r -= item.Value;
                    }
                }
            }

            LastSelectedMove = move = new BookDB.BookMove();
            return false;
        }

        /// <summary>
        /// 定跡の手を全部取得。
        /// Move.ValueにはBookEntry.Valueの値を入れておく。
        /// </summary>
        /// <param name="board">盤面</param>
        public List<BookDB.BookMove> GetBookMoves(Board board) {
            // ファイルが無かったり空だったりしたらfalse。
            if (!BookDB.IsReady()) {
                return new List<BookDB.BookMove>();
            }

            List<BookDB.BookMove> moves = BookDB.GetBookMoves(board);

            /*
            LastHitCount = moves.Sum(x => (int)x.Value);
            /*/ // ↓mono対策
            LastHitCount = 0;
            for (int i = 0, n = moves.Count; i < n; i++) LastHitCount += moves[i].Value;
            //*/
            // 降順ソート
            moves.Sort((x, y) => y.Value.CompareTo(x.Value));

            if (0 < moves.Count) {
                // イベントを発行
                var FoundBookMoves = this.FoundBookMoves;
                if (FoundBookMoves != null) {
                    bookReaderEventArgs.Board = board;
                    bookReaderEventArgs.BookMoves = moves;
                    FoundBookMoves(this, bookReaderEventArgs);
                }
            }

            return moves;
        }

        /// <summary>
        /// 前回の探索の統計情報とかを文字列化
        /// </summary>
        /// <returns>文字列</returns>
        public string GetDisplayString(Board board) {
            const int ValuePadWidth = 8;

            StringBuilder str = new StringBuilder();

            str.Append("定跡DB名:   ").AppendLine(Path.GetFileName(BookDB.FileName));
            str.Append("該当定跡数: ").AppendLine(LastHitCount.ToString().PadLeft(ValuePadWidth));
            str.Append("選択手:     ").AppendLine(LastSelectedMove.Move.ToString(board));
            str.Append("選択手値:   ").AppendLine(LastSelectedMove.Value.ToString());

            return str.ToString();
        }
    }
}

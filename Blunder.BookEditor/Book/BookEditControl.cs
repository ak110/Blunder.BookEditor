using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ShogiCore.Book {
    /// <summary>
    /// 定跡DBの表示・編集を行うコントロール。
    /// </summary>
    public partial class BookEditControl : UserControl {
        static readonly log4net.ILog logger = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        class BookEntry {
            public BookDB.BookEntryForWrite ForWrite;
        }
        Dictionary<ulong, List<BookEntry>> bookData = new Dictionary<ulong, List<BookEntry>>();

        /// <summary>
        /// 開いてるファイル名
        /// </summary>
        public string FileName { get; private set; }

        /// <summary>
        /// 全局面数
        /// </summary>
        public int AllPhases { get; private set; }
        /// <summary>
        /// 全手数
        /// </summary>
        public int AllMoves { get; private set; }

        /// <summary>
        /// 初期化
        /// </summary>
        public BookEditControl() {
            InitializeComponent();
        }

        /// <summary>
        /// 初期化。
        /// </summary>
        public void Open(string fileName) {
            FileName = fileName;

            using (BookDB db = new BookDB(fileName)) {
                db.PrepareRead();

                Dictionary<int, List<BookDB.BookEntryForWrite>> readBuffer = new Dictionary<int, List<BookDB.BookEntryForWrite>>();
                db.ReadAllEntries(readBuffer);
                bookData.Clear();
                foreach (var p in readBuffer) {
                    foreach (var w in p.Value) {
                        List<BookEntry> list;
                        if (!bookData.TryGetValue(w.Hash, out list)) {
                            bookData[w.Hash] = list = new List<BookEntry>();
                        }
                        list.Add(new BookEntry { ForWrite = w });
                    }
                }

                var t = db.GetCount();
                AllPhases = t.AllPhases;
                AllMoves = t.AllMoves;
            }

            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();
            try {
                BuildTreeView(treeView1.Nodes, new Board(), 2);
            } finally {
                treeView1.EndUpdate();
            }
            // 一番先頭の手を選択
            if (0 < treeView1.Nodes.Count) {
                treeView1.SelectedNode = treeView1.Nodes[0];
            }
        }

        /// <summary>
        /// 保存
        /// </summary>
        public void Save(string path) {
            Dictionary<int, List<BookDB.BookEntryForWrite>> writeBuffer = new Dictionary<int, List<BookDB.BookEntryForWrite>>();
            foreach (var p in bookData) {
                List<BookDB.BookEntryForWrite> list;
                int key = unchecked((int)p.Key) & BookDB.IndexMask;
                if (!writeBuffer.TryGetValue(key, out list)) {
                    writeBuffer[key] = list = new List<BookDB.BookEntryForWrite>();
                }
                list.AddRange(p.Value.Select(x => x.ForWrite).OrderByDescending(x => x.Value));
            }
            using (BookDB db = new BookDB(path)) {
                db.WriteAllEntries(writeBuffer);
            }
        }


        class TreeNodeEx : System.Windows.Forms.TreeNode {
            /// <summary>
            /// 展開済みならtrue
            /// </summary>
            public bool Expanded { get; set; }
            public TreeNodeEx(string text, bool expaned) : base(text) {
                Expanded = expaned;
            }
        }

        /// <summary>
        /// ツリービューの構築
        /// </summary>
        /// <remarks>追加したノード以下の全ノード数</remarks>
        private int BuildTreeView(TreeNodeCollection nodes, Board board, int depth) {
            if (depth <= 0) return 0;

            int count = 0;
            foreach (BookEntry entry in GetBookMoves(board)) {
                Move move = ShogiCore.Move.FromBinary(board, entry.ForWrite.Move);
                if (!board.IsLegalMove(ref move)) {
                    logger.Warn("定跡DBに違法な手？: " + move.ToString(board) + ", " + board.GetIllegalReason(move));
                    continue;
                }
                
                TreeNodeEx node = new TreeNodeEx(move.ToString(board), 1 < depth);
                node.Tag = entry;
                nodes.Add(node);

                // 子ノードへ再帰。
                board.Do(move);
                int childCount = BuildTreeView(node.Nodes, board, depth - 1);
                board.Undo(move);

                count += 1 + childCount;
            }
            return count;
        }

        /// <summary>
        /// 定跡の指し手を取得
        /// </summary>
        /// <param name="board"></param>
        /// <returns></returns>
        private IEnumerable<BookEntry> GetBookMoves(Board board) {
            List<BookEntry> list;
            if (bookData.TryGetValue(board.HashValue, out list)) {
                return list.OrderByDescending(x => x.ForWrite.Value);
            }
            return Enumerable.Empty<BookEntry>();
        }

        /// <summary>
        /// オンデマンドに展開。
        /// </summary>
        private void treeView1_BeforeExpand(object sender, TreeViewCancelEventArgs e) {
            foreach (var n in e.Node.Nodes) {
                var node = n as TreeNodeEx;
                if (node == null || node.Expanded) return;

                treeView1.BeginUpdate();
                try {
                    BuildTreeView(node.Nodes, GetNodeBoard(node), 2);
                    node.Expanded = true; // 展開済み
                } finally {
                    treeView1.EndUpdate();
                }
            }
        }

        /// <summary>
        /// 局面の表示
        /// </summary>
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e) {
            Board board = GetNodeBoard(e.Node);
            blunderViewControl1.Draw(board);
            numericUpDown1.Tag = e.Node.Tag;
            numericUpDown1.Value = ((BookEntry)e.Node.Tag).ForWrite.Value;
        }

        /// <summary>
        /// TreeNodeに対応する局面を生成
        /// </summary>
        private static Board GetNodeBoard(TreeNode node) {
            Board board = new Board();
            List<BookEntry> entries = new List<BookEntry>();
            for (; node != null; node = node.Parent) {
                entries.Add(((BookEntry)node.Tag));
            }
            entries.Reverse();
            foreach (var entry in entries) {
                Move move = ShogiCore.Move.FromBinary(board, entry.ForWrite.Move);
                board.Do(move);
            }
            return board;
        }

        /// <summary>
        /// 値が変更された
        /// </summary>
        private void numericUpDown1_ValueChanged(object sender, EventArgs e) {
            ((BookEntry)numericUpDown1.Tag).ForWrite.Value = (ulong)numericUpDown1.Value;
        }
    }
}

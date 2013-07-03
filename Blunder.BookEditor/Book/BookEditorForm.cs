using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace ShogiCore.Book {
    /// <summary>
    /// 定跡DB編集ダイアログ
    /// </summary>
    public partial class BookEditorForm : Form {
        string initialFileName;

        public BookEditorForm() {
            InitializeComponent();
        }

        public BookEditorForm(string fileName) {
            InitializeComponent();
            initialFileName = fileName;
        }

        /// <summary>
        /// 初期化。
        /// </summary>
        private void BookEditorForm_Shown(object sender, EventArgs e) {
            Open(initialFileName);
        }

        /// <summary>
        /// 後始末。
        /// </summary>
        private void BookEditForm_FormClosing(object sender, FormClosingEventArgs e) {

        }

        /// <summary>
        /// 定跡DBを開く
        /// </summary>
        private void Open(string fileName) {
            if (string.IsNullOrEmpty(fileName)) {
                Text = "Blunder.BookEditor";
            } else {
                bookEditControl1.Open(fileName);
                Text = fileName + " - Blunder.BookEditor " +
                    bookEditControl1.AllPhases.ToString("#,##0") + "局面 " +
                    bookEditControl1.AllMoves.ToString("#,##0") + "手";
            }
        }

        #region メニューバー

        private void ファイルを開くOToolStripMenuItem_Click(object sender, EventArgs e) {
            if (openFileDialog1.ShowDialog(this) == DialogResult.OK) {
                string dir = System.IO.Path.GetDirectoryName(bookEditControl1.FileName);
                openFileDialog1.InitialDirectory = System.IO.Directory.Exists(dir) ? dir : AppDomain.CurrentDomain.BaseDirectory;
                Open(openFileDialog1.FileName);
            }
        }

        private void 終了XToolStripMenuItem_Click(object sender, EventArgs e) {
            Close();
        }

        #endregion

        private void BookEditorForm_DragOver(object sender, DragEventArgs e) {
            e.Effect = DragDropEffects.None;

            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files.Length == 1 &&
                    string.Compare(Path.GetExtension(files[0]), ".db",
                    StringComparison.OrdinalIgnoreCase) == 0) {
                    e.Effect = DragDropEffects.Copy;
                }
            }
        }

        private void BookEditorForm_DragDrop(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files.Length == 1 &&
                    string.Compare(Path.GetExtension(files[0]), ".db",
                    StringComparison.OrdinalIgnoreCase) == 0) {
                    Open(files[0]);
                }
            }
        }

        private void 上書き保存SToolStripMenuItem_Click(object sender, EventArgs e) {
            bookEditControl1.Save(bookEditControl1.FileName);
        }

        private void 名前を付けて保存AToolStripMenuItem_Click(object sender, EventArgs e) {
            using (SaveFileDialog dialog = new SaveFileDialog()) {
                dialog.Title = "名前を付けて保存";
                dialog.Filter = "定跡DB (*.db)|*.db|全てのファイル (*.*)|*.*";
                dialog.InitialDirectory = Path.GetDirectoryName(bookEditControl1.FileName);
                dialog.FileName = Path.GetFileNameWithoutExtension(bookEditControl1.FileName);
                dialog.DefaultExt = ".db";
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    bookEditControl1.Save(dialog.FileName);
                }
            }
        }
    }
}
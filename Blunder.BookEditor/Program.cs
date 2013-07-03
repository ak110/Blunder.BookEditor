using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ShogiCore.Book;

namespace ShogiCore.BookEditor {
    static class Program {
        /// <summary>
        /// アプリケーションのメイン エントリ ポイントです。
        /// </summary>
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new BookEditorForm());
        }
    }
}

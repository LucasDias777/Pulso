using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Pulso
{
    // Leitura mínima de SQLite pelo winsqlite3.dll que já vem no Windows 10/11 (Cursor e OpenCode guardam
    // a sessão em SQLite). Só leitura; primeiro normal (enxerga o WAL de um token recém-renovado),
    // depois immutable=1 (editor fechado, sem -shm).
    static class Sqlite
    {
        const int OK = 0, LINHA = 100, SO_LEITURA = 0x1, URI = 0x40, SEM_MUTEX = 0x8000;
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_open_v2(byte[] arquivo, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr st, IntPtr resto);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_step(IntPtr st);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_finalize(IntPtr st);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern IntPtr sqlite3_column_text(IntPtr st, int col);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_column_bytes(IntPtr st, int col);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)] static extern int sqlite3_column_count(IntPtr st);

        static byte[] Z(string s) { var b = System.Text.Encoding.UTF8.GetBytes(s); Array.Resize(ref b, b.Length + 1); return b; }

        static IntPtr Abrir(string arquivo, string teste)
        {
            IntPtr db;
            if (sqlite3_open_v2(Z(arquivo), out db, SO_LEITURA | SEM_MUTEX, IntPtr.Zero) == OK && Funciona(db, teste)) return db;
            if (db != IntPtr.Zero) sqlite3_close(db);
            string uri = "file:///" + arquivo.Replace('\\', '/').Replace("#", "%23").Replace("?", "%3F") + "?immutable=1";
            if (sqlite3_open_v2(Z(uri), out db, SO_LEITURA | SEM_MUTEX | URI, IntPtr.Zero) == OK && Funciona(db, teste)) return db;
            if (db != IntPtr.Zero) sqlite3_close(db);
            return IntPtr.Zero;
        }

        static bool Funciona(IntPtr db, string teste)
        {
            IntPtr st;
            if (sqlite3_prepare_v2(db, Z(teste), -1, out st, IntPtr.Zero) != OK) return false;
            try { int r = sqlite3_step(st); return r == LINHA || r == 101; }
            finally { sqlite3_finalize(st); }
        }

        // Linhas de uma consulta (cada coluna como texto); a consulta não recebe parâmetro do usuário
        public static List<string[]> Consultar(string arquivo, string teste, string sql)
        {
            var r = new List<string[]>();
            IntPtr db = Abrir(arquivo, teste);
            if (db == IntPtr.Zero) return null;
            try
            {
                IntPtr st;
                if (sqlite3_prepare_v2(db, Z(sql), -1, out st, IntPtr.Zero) != OK) return null;
                try
                {
                    while (sqlite3_step(st) == LINHA)
                    {
                        int n = sqlite3_column_count(st);
                        var linha = new string[n];
                        for (int i = 0; i < n; i++)
                        {
                            IntPtr p = sqlite3_column_text(st, i);
                            int tam = sqlite3_column_bytes(st, i);
                            if (p == IntPtr.Zero) continue;
                            var b = new byte[tam];
                            Marshal.Copy(p, b, 0, tam);
                            linha[i] = System.Text.Encoding.UTF8.GetString(b);
                        }
                        r.Add(linha);
                    }
                }
                finally { sqlite3_finalize(st); }
            }
            finally { sqlite3_close(db); }
            return r;
        }
    }
}

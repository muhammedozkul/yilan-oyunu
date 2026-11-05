using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace YilanOyunu
{
    public partial class Form1 : Form
    {
        // ---- Tahta ölçüleri ----
        const int Sutun = 30, Satir = 20, Hucre = 30;
        const int TahtaX = 30, TahtaY = 100;
        const double BaslangicAdimMs = 140, EnHizliAdimMs = 60;
        const double AltinSuresi = 6.0;

        enum Durum { Giris, Oynuyor, Duraklatildi, Bitti }
        Durum durum = Durum.Giris;

        // ---- Yılan ----
        List<Point> govde = new List<Point>();
        List<Point> oncekiGovde = new List<Point>();   // akıcı kayma için bir önceki adım
        Point yon = new Point(1, 0);
        Point sonIstenenYon = new Point(1, 0);
        Queue<Point> yonKuyrugu = new Queue<Point>();

        // ---- Yemler ----
        Point elma;
        Point altin;
        bool altinVar;
        double altinKalan;

        // ---- Skor ve zaman ----
        int skor, rekor, yenen;
        bool yeniRekor;
        double adimMs = BaslangicAdimMs, birikim;
        Stopwatch saat = new Stopwatch();
        double sonZaman, toplamZaman, olumSarsinti;
        Random rnd = new Random();
        string rekorDosyasi = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rekor.txt");

        // ---- Efektler ----
        class Parcacik { public float X, Y, VX, VY, Omur, MaxOmur, Boy; public Color Renk; }
        class YuzenYazi { public float X, Y, Omur; public string Metin; public Color Renk; }
        List<Parcacik> parcaciklar = new List<Parcacik>();
        List<YuzenYazi> yazilar = new List<YuzenYazi>();

        // ---- Renkler ve yazı tipleri ----
        static readonly Color BasRengi = Color.FromArgb(124, 77, 255);
        static readonly Color KuyrukRengi = Color.FromArgb(64, 196, 255);
        static readonly Color CimenAcik = Color.FromArgb(52, 136, 88);
        static readonly Color CimenKoyu = Color.FromArgb(46, 125, 80);
        static readonly Color Altin = Color.FromArgb(255, 196, 64);

        Font baslikFont = new Font("Segoe UI", 22F, FontStyle.Bold);
        Font etiketFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        Font degerFont = new Font("Segoe UI", 18F, FontStyle.Bold);
        Font devFont = new Font("Segoe UI", 54F, FontStyle.Bold);
        Font buyukFont = new Font("Segoe UI", 34F, FontStyle.Bold);
        Font ortaFont = new Font("Segoe UI", 15F, FontStyle.Bold);
        Font kucukFont = new Font("Segoe UI", 11F);
        Font yaziFont = new Font("Segoe UI", 14F, FontStyle.Bold);

        public Form1()
        {
            InitializeComponent();
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            RekoruOku();
            Deactivate += delegate { if (durum == Durum.Oynuyor) durum = Durum.Duraklatildi; };
            saat.Start();
            timer1.Start();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OyunuBaslat();
        }

        // =====================================================================
        //  OYUN MANTIĞI
        // =====================================================================

        void OyunuBaslat()
        {
            govde.Clear();
            for (int i = 0; i < 4; i++) govde.Add(new Point(8 - i, Satir / 2));
            oncekiGovde = new List<Point>(govde);
            yon = new Point(1, 0);
            sonIstenenYon = yon;
            yonKuyrugu.Clear();

            skor = 0; yenen = 0; yeniRekor = false;
            adimMs = BaslangicAdimMs; birikim = 0;
            altinVar = false;
            parcaciklar.Clear(); yazilar.Clear();
            elma = BosHucre();

            durum = Durum.Oynuyor;
            button1.Visible = false;
            Focus();
        }

        Point BosHucre()
        {
            while (true)
            {
                Point p = new Point(rnd.Next(Sutun), rnd.Next(Satir));
                if (govde.Contains(p)) continue;
                if (p == elma && durum == Durum.Oynuyor) continue;
                if (altinVar && p == altin) continue;
                return p;
            }
        }

        void YonEkle(Point y)
        {
            // Ters yöne dönmek yok; aynı yönü tekrar eklemenin anlamı yok.
            if (y.X == -sonIstenenYon.X && y.Y == -sonIstenenYon.Y) return;
            if (y == sonIstenenYon) return;
            if (yonKuyrugu.Count >= 3) return;
            yonKuyrugu.Enqueue(y);
            sonIstenenYon = y;
        }

        void Adim()
        {
            if (yonKuyrugu.Count > 0) yon = yonKuyrugu.Dequeue();

            Point bas = govde[0];
            Point yeni = new Point(bas.X + yon.X, bas.Y + yon.Y);

            if (yeni.X < 0 || yeni.Y < 0 || yeni.X >= Sutun || yeni.Y >= Satir) { Ol(); return; }

            bool elmaYedi = yeni == elma;
            bool altinYedi = altinVar && yeni == altin;
            bool buyur = elmaYedi || altinYedi;

            // Büyümüyorsa kuyruk ucu bu adımda boşalır, oraya girmek serbest.
            int kontrol = buyur ? govde.Count : govde.Count - 1;
            for (int i = 0; i < kontrol; i++)
                if (govde[i] == yeni) { Ol(); return; }

            oncekiGovde = new List<Point>(govde);
            govde.Insert(0, yeni);
            if (buyur) oncekiGovde.Add(oncekiGovde[oncekiGovde.Count - 1]);
            else govde.RemoveAt(govde.Count - 1);

            PointF m = Merkez(yeni);
            if (elmaYedi)
            {
                skor += 1; yenen++;
                Patlat(m, Color.FromArgb(255, 82, 82), 14);
                YaziEkle(m, "+1", Color.White);
                elma = BosHucre();
                adimMs = Math.Max(EnHizliAdimMs, BaslangicAdimMs - yenen * 3);
                if (!altinVar && yenen >= 3 && rnd.Next(5) == 0)
                {
                    altin = BosHucre();
                    altinVar = true;
                    altinKalan = AltinSuresi;
                }
            }
            if (altinYedi)
            {
                skor += 5;
                altinVar = false;
                Patlat(m, Altin, 26);
                YaziEkle(m, "+5", Altin);
            }
        }

        void Ol()
        {
            durum = Durum.Bitti;
            birikim = adimMs;           // yılan son karede tam hücrede dursun
            olumSarsinti = 0.35;
            Patlat(Merkez(govde[0]), BasRengi, 30);
            if (skor > rekor)
            {
                rekor = skor;
                yeniRekor = true;
                RekoruYaz();
            }
            button1.Text = "TEKRAR OYNA";
            button1.Location = new Point(360, 520);
            button1.Visible = true;
            button1.Focus();
        }

        int Seviye { get { return 1 + Math.Min(yenen, 27) / 3; } }

        void RekoruOku()
        {
            try
            {
                if (File.Exists(rekorDosyasi)) int.TryParse(File.ReadAllText(rekorDosyasi).Trim(), out rekor);
            }
            catch { rekor = 0; }
        }

        void RekoruYaz()
        {
            try { File.WriteAllText(rekorDosyasi, rekor.ToString()); }
            catch { /* yazılamazsa rekor yalnız bu oturumda kalır */ }
        }

        // =====================================================================
        //  ZAMANLAYICI VE KLAVYE
        // =====================================================================

        private void timer1_Tick(object sender, EventArgs e)
        {
            double simdi = saat.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.1, simdi - sonZaman);
            sonZaman = simdi;
            toplamZaman += dt;

            if (durum == Durum.Oynuyor)
            {
                birikim += dt * 1000;
                while (birikim >= adimMs && durum == Durum.Oynuyor)
                {
                    birikim -= adimMs;
                    Adim();
                }
                if (altinVar)
                {
                    altinKalan -= dt;
                    if (altinKalan <= 0) altinVar = false;
                }
            }

            for (int i = parcaciklar.Count - 1; i >= 0; i--)
            {
                Parcacik p = parcaciklar[i];
                p.X += p.VX * dt; p.Y += p.VY * dt;
                p.VY += 180 * dt;
                p.Omur -= dt;
                if (p.Omur <= 0) parcaciklar.RemoveAt(i);
            }
            for (int i = yazilar.Count - 1; i >= 0; i--)
            {
                yazilar[i].Y -= 45 * dt;
                yazilar[i].Omur -= dt;
                if (yazilar[i].Omur <= 0) yazilar.RemoveAt(i);
            }
            if (olumSarsinti > 0) olumSarsinti -= dt;

            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            bool duraklatTusu = k == Keys.P || k == Keys.Space || k == Keys.Escape;

            if (durum == Durum.Oynuyor)
            {
                if (k == Keys.Up || k == Keys.W) { YonEkle(new Point(0, -1)); return true; }
                if (k == Keys.Down || k == Keys.S) { YonEkle(new Point(0, 1)); return true; }
                if (k == Keys.Left || k == Keys.A) { YonEkle(new Point(-1, 0)); return true; }
                if (k == Keys.Right || k == Keys.D) { YonEkle(new Point(1, 0)); return true; }
                if (duraklatTusu) { durum = Durum.Duraklatildi; return true; }
            }
            else if (durum == Durum.Duraklatildi)
            {
                if (duraklatTusu || k == Keys.Enter) { durum = Durum.Oynuyor; return true; }
            }
            else if (k == Keys.Enter || k == Keys.Space)
            {
                OyunuBaslat();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // =====================================================================
        //  EFEKTLER
        // =====================================================================

        void Patlat(PointF c, Color renk, int adet)
        {
            for (int i = 0; i < adet; i++)
            {
                double aci = rnd.NextDouble() * Math.PI * 2;
                float hiz = 60 + (float)rnd.NextDouble() * 140;
                float omur = 0.5f + (float)rnd.NextDouble() * 0.4f;
                parcaciklar.Add(new Parcacik
                {
                    X = c.X, Y = c.Y,
                    VX = (float)Math.Cos(aci) * hiz,
                    VY = (float)Math.Sin(aci) * hiz - 40,
                    Omur = omur, MaxOmur = omur,
                    Boy = 3 + (float)rnd.NextDouble() * 4,
                    Renk = renk
                });
            }
        }

        void YaziEkle(PointF c, string metin, Color renk)
        {
            yazilar.Add(new YuzenYazi { X = c.X, Y = c.Y - 10, Omur = 1f, Metin = metin, Renk = renk });
        }

        // =====================================================================
        //  ÇİZİM
        // =====================================================================

        static PointF Merkez(Point h)
        {
            return new PointF(TahtaX + h.X * Hucre + Hucre / 2f, TahtaY + h.Y * Hucre + Hucre / 2f);
        }

        static Color Karistir(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        static Color Acik(Color c, float t) { return Karistir(c, Color.White, t); }

        static GraphicsPath YuvarlakDikdortgen(RectangleF r, float yaricap)
        {
            GraphicsPath p = new GraphicsPath();
            float d = yaricap * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void OrtaliYaz(Graphics g, string metin, Font f, Color renk, float x, float y)
        {
            SizeF s = g.MeasureString(metin, f);
            using (SolidBrush b = new SolidBrush(renk))
                g.DrawString(metin, f, b, x - s.Width / 2, y - s.Height / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            ArkaPlanCiz(g);
            UstBarCiz(g);

            GraphicsState st = g.Save();
            if (olumSarsinti > 0)
            {
                float s = (float)olumSarsinti * 20;
                g.TranslateTransform((float)(rnd.NextDouble() - 0.5) * s, (float)(rnd.NextDouble() - 0.5) * s);
            }
            TahtaCiz(g);
            if (durum != Durum.Giris)
            {
                ElmaCiz(g, Merkez(elma));
                if (altinVar) AltinCiz(g, Merkez(altin));
                YilanCiz(g);
            }
            EfektleriCiz(g);
            g.Restore(st);

            if (durum == Durum.Giris) GirisEkraniCiz(g);
            else if (durum == Durum.Duraklatildi) DuraklatildiCiz(g);
            else if (durum == Durum.Bitti) BittiEkraniCiz(g);
        }

        void ArkaPlanCiz(Graphics g)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(16, 38, 30), Color.FromArgb(6, 16, 13), 90f))
                g.FillRectangle(b, ClientRectangle);

            // Hafif yaprak benekleri
            using (SolidBrush b = new SolidBrush(Color.FromArgb(14, 120, 255, 170)))
            {
                g.FillEllipse(b, -80, 500, 260, 260);
                g.FillEllipse(b, 820, -60, 220, 220);
                g.FillEllipse(b, 700, 600, 180, 180);
            }
        }

        void UstBarCiz(Graphics g)
        {
            RectangleF r = new RectangleF(30, 16, 900, 68);
            using (GraphicsPath p = YuvarlakDikdortgen(r, 16))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            using (Pen kenar = new Pen(Color.FromArgb(50, 120, 255, 170), 1.5f))
            {
                g.FillPath(b, p);
                g.DrawPath(kenar, p);
            }

            // Logo: küçük kıvrık yılan + YILAN yazısı
            PointF[] logo = new PointF[8];
            for (int i = 0; i < logo.Length; i++)
                logo[i] = new PointF(52 + i * 5, 50 + (float)Math.Sin(i * 0.9 + toplamZaman * 3) * 5);
            using (Pen p = new Pen(KuyrukRengi, 7) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, logo);
            using (SolidBrush b = new SolidBrush(BasRengi))
                g.FillEllipse(b, logo[7].X - 6, logo[7].Y - 6, 12, 12);
            using (SolidBrush b = new SolidBrush(Color.White))
                g.DrawString("YILAN", baslikFont, b, 100, 30);

            IstatistikCiz(g, 300, "SKOR", skor.ToString(), Color.White);
            IstatistikCiz(g, 450, "UZUNLUK", (durum == Durum.Giris ? 0 : govde.Count).ToString(), Color.White);
            IstatistikCiz(g, 600, "REKOR", rekor.ToString(), Altin);
            IstatistikCiz(g, 750, "HIZ", durum == Durum.Giris ? "-" : Seviye.ToString(), KuyrukRengi);

            using (SolidBrush b = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
                g.DrawString("P: Duraklat", etiketFont, b, 838, 43);
        }

        void IstatistikCiz(Graphics g, float x, string etiket, string deger, Color renk)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 200, 230, 215)))
                g.DrawString(etiket, etiketFont, b, x, 25);
            using (SolidBrush b = new SolidBrush(renk))
                g.DrawString(deger, degerFont, b, x - 3, 41);
        }

        void TahtaCiz(Graphics g)
        {
            RectangleF r = new RectangleF(TahtaX, TahtaY, Sutun * Hucre, Satir * Hucre);

            // Gölge
            using (GraphicsPath p = YuvarlakDikdortgen(new RectangleF(r.X + 4, r.Y + 8, r.Width, r.Height), 14))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                g.FillPath(b, p);

            using (GraphicsPath p = YuvarlakDikdortgen(r, 14))
            {
                GraphicsState st = g.Save();
                g.SetClip(p);
                using (SolidBrush koyu = new SolidBrush(CimenKoyu))
                using (SolidBrush acik = new SolidBrush(CimenAcik))
                {
                    g.FillRectangle(koyu, r);
                    for (int y = 0; y < Satir; y++)
                        for (int x = 0; x < Sutun; x++)
                            if ((x + y) % 2 == 0)
                                g.FillRectangle(acik, TahtaX + x * Hucre, TahtaY + y * Hucre, Hucre, Hucre);
                }
                // Kenarlara doğru hafif karartma
                using (GraphicsPath ic = new GraphicsPath())
                {
                    ic.AddRectangle(r);
                    using (PathGradientBrush pg = new PathGradientBrush(ic))
                    {
                        pg.CenterColor = Color.FromArgb(0, 0, 0, 0);
                        pg.SurroundColors = new Color[] { Color.FromArgb(70, 0, 20, 10) };
                        pg.FocusScales = new PointF(0.8f, 0.75f);
                        g.FillRectangle(pg, r);
                    }
                }
                g.Restore(st);
                using (Pen kenar = new Pen(Color.FromArgb(120, 140, 255, 190), 2))
                    g.DrawPath(kenar, p);
            }
        }

        void ElmaCiz(Graphics g, PointF c)
        {
            float s = 1 + 0.07f * (float)Math.Sin(toplamZaman * 6);
            float r = 10.5f * s;

            using (SolidBrush golge = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillEllipse(golge, c.X - r + 2, c.Y - r + 4, r * 2, r * 2);

            RectangleF govdeR = new RectangleF(c.X - r, c.Y - r + 1, r * 2, r * 2);
            using (LinearGradientBrush b = new LinearGradientBrush(govdeR,
                Color.FromArgb(255, 96, 86), Color.FromArgb(196, 24, 40), 60f))
                g.FillEllipse(b, govdeR);
            using (SolidBrush parlak = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                g.FillEllipse(parlak, c.X - r * 0.55f, c.Y - r * 0.5f, r * 0.55f, r * 0.4f);

            using (Pen sap = new Pen(Color.FromArgb(110, 70, 35), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(sap, c.X, c.Y - r + 2, c.X + 1.5f, c.Y - r - 4);
            using (GraphicsPath yaprak = new GraphicsPath())
            {
                float lx = c.X + 2, ly = c.Y - r - 2;
                yaprak.AddBezier(lx, ly, lx + 3, ly - 6, lx + 9, ly - 6, lx + 11, ly - 4);
                yaprak.AddBezier(lx + 11, ly - 4, lx + 8, ly + 1, lx + 3, ly + 2, lx, ly);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 210, 90)))
                    g.FillPath(b, yaprak);
            }
        }

        void AltinCiz(Graphics g, PointF c)
        {
            float nabiz = 1 + 0.12f * (float)Math.Sin(toplamZaman * 8);
            bool sonSaniyeler = altinKalan < 2;
            if (sonSaniyeler && Math.Sin(toplamZaman * 25) < 0) return;   // kaybolmadan önce yanıp söner

            using (SolidBrush isilti = new SolidBrush(Color.FromArgb(60, 255, 220, 100)))
                g.FillEllipse(isilti, c.X - 20 * nabiz, c.Y - 20 * nabiz, 40 * nabiz, 40 * nabiz);

            PointF[] yildiz = new PointF[10];
            double donus = toplamZaman * 1.5;
            for (int i = 0; i < 10; i++)
            {
                double a = donus + i * Math.PI / 5 - Math.PI / 2;
                float rr = (i % 2 == 0 ? 13f : 6f) * nabiz;
                yildiz[i] = new PointF(c.X + (float)Math.Cos(a) * rr, c.Y + (float)Math.Sin(a) * rr);
            }
            using (SolidBrush b = new SolidBrush(Altin))
                g.FillPolygon(b, yildiz);
            using (Pen p = new Pen(Color.FromArgb(255, 248, 210), 1.5f))
                g.DrawPolygon(p, yildiz);

            // Kalan süre halkası
            float oran = (float)(altinKalan / AltinSuresi);
            using (Pen halka = new Pen(Color.FromArgb(200, 255, 236, 160), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(halka, c.X - 17, c.Y - 17, 34, 34, -90, 360 * oran);
        }

        void YilanCiz(Graphics g)
        {
            int n = govde.Count;
            float t = (float)Math.Min(1, birikim / adimMs);
            PointF[] p = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                PointF a = Merkez(oncekiGovde[i]), b = Merkez(govde[i]);
                p[i] = new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }

            // Gölge
            for (int i = n - 1; i >= 1; i--)
            {
                float k = i / (float)(n - 1);
                using (Pen pen = new Pen(Color.FromArgb(55, 0, 0, 0), 24 - 8 * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, p[i].X + 3, p[i].Y + 5, p[i - 1].X + 3, p[i - 1].Y + 5);
            }

            // Gövde: kuyruktan başa doğru renk geçişi ve incelme
            for (int i = n - 1; i >= 1; i--)
            {
                float k = i / (float)(n - 1);
                float kalinlik = 24 - 8 * k;
                Color renk = Karistir(BasRengi, KuyrukRengi, k);
                using (Pen pen = new Pen(renk, kalinlik) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, p[i], p[i - 1]);
            }
            // Sırt şeridi
            for (int i = n - 1; i >= 1; i--)
            {
                float k = i / (float)(n - 1);
                Color renk = Color.FromArgb(110, Acik(Karistir(BasRengi, KuyrukRengi, k), 0.6f));
                using (Pen pen = new Pen(renk, (24 - 8 * k) * 0.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, p[i], p[i - 1]);
            }
            // Pullar
            for (int i = 2; i < n; i += 2)
            {
                float k = i / (float)(n - 1);
                float r = 3.2f - 1.2f * k;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 20, 10, 60)))
                    g.FillEllipse(b, p[i].X - r, p[i].Y - r, r * 2, r * 2);
            }

            // Baş
            Point d = new Point(govde[0].X - govde[1].X, govde[0].Y - govde[1].Y);
            PointF h = p[0];
            PointF dik = new PointF(-d.Y, d.X);

            if (durum == Durum.Oynuyor && (toplamZaman % 1.4) < 0.28)
            {
                using (Pen dil = new Pen(Color.FromArgb(255, 70, 100), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    PointF u1 = new PointF(h.X + d.X * 13, h.Y + d.Y * 13);
                    PointF u2 = new PointF(h.X + d.X * 22, h.Y + d.Y * 22);
                    g.DrawLine(dil, u1, u2);
                    g.DrawLine(dil, u2.X, u2.Y, u2.X + d.X * 4 + dik.X * 3, u2.Y + d.Y * 4 + dik.Y * 3);
                    g.DrawLine(dil, u2.X, u2.Y, u2.X + d.X * 4 - dik.X * 3, u2.Y + d.Y * 4 - dik.Y * 3);
                }
            }

            RectangleF basR = new RectangleF(h.X - 15, h.Y - 15, 30, 30);
            using (LinearGradientBrush b = new LinearGradientBrush(basR, Acik(BasRengi, 0.25f), Karistir(BasRengi, Color.Black, 0.15f), 45f))
                g.FillEllipse(b, basR);

            for (int s = -1; s <= 1; s += 2)
            {
                float gx = h.X + d.X * 4 + dik.X * 7 * s;
                float gy = h.Y + d.Y * 4 + dik.Y * 7 * s;
                if (durum == Durum.Bitti)
                {
                    using (Pen x = new Pen(Color.White, 2.2f))
                    {
                        g.DrawLine(x, gx - 3.5f, gy - 3.5f, gx + 3.5f, gy + 3.5f);
                        g.DrawLine(x, gx - 3.5f, gy + 3.5f, gx + 3.5f, gy - 3.5f);
                    }
                    continue;
                }
                using (SolidBrush beyaz = new SolidBrush(Color.White))
                    g.FillEllipse(beyaz, gx - 5, gy - 5, 10, 10);
                using (SolidBrush siyah = new SolidBrush(Color.FromArgb(20, 16, 40)))
                    g.FillEllipse(siyah, gx + d.X * 1.6f - 2.6f, gy + d.Y * 1.6f - 2.6f, 5.2f, 5.2f);
            }
        }

        void EfektleriCiz(Graphics g)
        {
            foreach (Parcacik p in parcaciklar)
            {
                int a = (int)(255 * Math.Max(0, p.Omur / p.MaxOmur));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, p.Renk)))
                    g.FillEllipse(b, p.X - p.Boy / 2, p.Y - p.Boy / 2, p.Boy, p.Boy);
            }
            foreach (YuzenYazi y in yazilar)
            {
                int a = (int)(255 * Math.Max(0, Math.Min(1, y.Omur * 1.5f)));
                OrtaliYaz(g, y.Metin, yaziFont, Color.FromArgb(a, y.Renk), y.X, y.Y);
            }
        }

        void PerdeCiz(Graphics g, int opaklik)
        {
            RectangleF r = new RectangleF(TahtaX, TahtaY, Sutun * Hucre, Satir * Hucre);
            using (GraphicsPath p = YuvarlakDikdortgen(r, 14))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(opaklik, 6, 18, 14)))
                g.FillPath(b, p);
        }

        void GirisEkraniCiz(Graphics g)
        {
            PerdeCiz(g, 120);
            float cx = TahtaX + Sutun * Hucre / 2f;

            // Dalgalanan süs yılanı
            int adet = 26;
            PointF[] p = new PointF[adet];
            for (int i = 0; i < adet; i++)
            {
                float x = cx - 190 + i * 15;
                p[i] = new PointF(x, 205 + (float)Math.Sin(i * 0.45 - toplamZaman * 4) * 20);
            }
            for (int i = 0; i < adet - 1; i++)
            {
                float k = 1 - i / (float)(adet - 1);
                using (Pen pen = new Pen(Karistir(BasRengi, KuyrukRengi, k), 14 + 10 * (1 - k)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, p[i], p[i + 1]);
            }
            PointF bas = p[adet - 1];
            using (SolidBrush b = new SolidBrush(BasRengi))
                g.FillEllipse(b, bas.X - 15, bas.Y - 15, 30, 30);
            using (SolidBrush beyaz = new SolidBrush(Color.White))
            {
                g.FillEllipse(beyaz, bas.X, bas.Y - 11, 9, 9);
                g.FillEllipse(beyaz, bas.X, bas.Y + 2, 9, 9);
            }
            using (SolidBrush siyah = new SolidBrush(Color.FromArgb(20, 16, 40)))
            {
                g.FillEllipse(siyah, bas.X + 4, bas.Y - 9, 4.5f, 4.5f);
                g.FillEllipse(siyah, bas.X + 4, bas.Y + 4, 4.5f, 4.5f);
            }
            ElmaCiz(g, new PointF(bas.X + 55, bas.Y));

            OrtaliYaz(g, "YILAN", devFont, Color.White, cx, 305);
            OrtaliYaz(g, "Elmaları topla, uza; duvara ve kendine çarpma!", ortaFont, Color.FromArgb(220, 230, 255, 240), cx, 375);
            OrtaliYaz(g, "Yön tuşları / WASD ile yönet  •  P ile duraklat  •  Altın yıldız +5 puan", kucukFont, Color.FromArgb(170, 220, 240, 230), cx, 415);
            if (rekor > 0)
                OrtaliYaz(g, "Rekorun: " + rekor, kucukFont, Altin, cx, 445);
            OrtaliYaz(g, "Enter ile de başlayabilirsin", kucukFont, Color.FromArgb(130, 220, 240, 230), cx, 560);
        }

        void DuraklatildiCiz(Graphics g)
        {
            PerdeCiz(g, 150);
            float cx = TahtaX + Sutun * Hucre / 2f, cy = TahtaY + Satir * Hucre / 2f;
            using (SolidBrush b = new SolidBrush(Color.White))
            {
                g.FillRectangle(b, cx - 22, cy - 90, 14, 46);
                g.FillRectangle(b, cx + 8, cy - 90, 14, 46);
            }
            OrtaliYaz(g, "DURAKLATILDI", buyukFont, Color.White, cx, cy);
            OrtaliYaz(g, "Devam etmek için P, Boşluk ya da Enter", kucukFont, Color.FromArgb(190, 220, 240, 230), cx, cy + 50);
        }

        void BittiEkraniCiz(Graphics g)
        {
            PerdeCiz(g, 165);
            float cx = TahtaX + Sutun * Hucre / 2f;

            OrtaliYaz(g, "OYUN BİTTİ", devFont, Color.FromArgb(255, 110, 110), cx, 220);

            RectangleF kart = new RectangleF(cx - 220, 280, 440, 200);
            using (GraphicsPath p = YuvarlakDikdortgen(kart, 18))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            using (Pen kenar = new Pen(Color.FromArgb(70, 140, 255, 190), 1.5f))
            {
                g.FillPath(b, p);
                g.DrawPath(kenar, p);
            }

            SonucSatiri(g, kart, 0, "Skor", skor.ToString(), Color.White);
            SonucSatiri(g, kart, 1, "Uzunluk", govde.Count.ToString(), KuyrukRengi);
            SonucSatiri(g, kart, 2, "Rekor", rekor.ToString(), Altin);

            if (yeniRekor)
            {
                float s = 1 + 0.06f * (float)Math.Sin(toplamZaman * 7);
                GraphicsState st = g.Save();
                g.TranslateTransform(cx, 460);
                g.ScaleTransform(s, s);
                OrtaliYaz(g, "★ YENİ REKOR! ★", ortaFont, Altin, 0, 0);
                g.Restore(st);
            }
        }

        void SonucSatiri(Graphics g, RectangleF kart, int sira, string etiket, string deger, Color renk)
        {
            float y = kart.Y + 24 + sira * 46;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 220, 240, 230)))
                g.DrawString(etiket, ortaFont, b, kart.X + 40, y);
            SizeF s = g.MeasureString(deger, degerFont);
            using (SolidBrush b = new SolidBrush(renk))
                g.DrawString(deger, degerFont, b, kart.Right - 40 - s.Width, y - 3);
        }
    }
}

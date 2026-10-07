using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        Bitmap loaded, processed;
        string[] coinNames = { "5 centavo (holed)", "10 centavo", "25 centavo", "1 peso", "5 pesos" };
        double[] coinValues = { 0.05, 0.10, 0.25, 1.00, 5.00 };

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            label4.AutoSize = true;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
        }

        //coin button
        private void button1_Click(object sender, EventArgs e)
        {
            if (loaded == null)
            {
                MessageBox.Show("Please load an image first!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int w = loaded.Width, h = loaded.Height;

            int[] gray = GetGray(loaded);

            int t = OtsuThreshold(gray);
            bool[] fg = new bool[w * h];
            for (int i = 0; i < fg.Length; i++) fg[i] = gray[i] < t;

            bool[] outside = FloodOutside(fg, w, h);
            bool[] filled = new bool[w * h];
            for (int i = 0; i < filled.Length; i++) filled[i] = !outside[i];

            List<Coin> all = LabelComponents(filled, w, h);
            if (all.Count == 0)
            {
                ShowNone();
                return;
            }

            
            int maxArea = all.Max(c => c.Area);
            List<Coin> coins = new List<Coin>();
            foreach (Coin c in all)
            {
                double bw = c.MaxX - c.MinX + 1, bh = c.MaxY - c.MinY + 1;
                double aspect = bw / bh;
                if (c.Area < 0.10 * maxArea) continue;      
                if (aspect < 0.7 || aspect > 1.4) continue;
                c.Diameter = Math.Sqrt(4.0 * c.Area / Math.PI);
                c.HasHole = DetectHole(c, fg, w, h);
                coins.Add(c);
            }

            if (coins.Count == 0)
            {
                ShowNone();
                return;
            }

            double maxDiam = coins.Max(c => c.Diameter);
            foreach (Coin c in coins)
                if (c.HasHole && c.Diameter > 0.75 * maxDiam) c.HasHole = false;

            foreach (Coin c in coins) if (c.HasHole) c.Type = 0;

            List<Coin> solid = coins.Where(c => !c.HasHole).ToList();
            if (solid.Count > 0)
            {
                double[] centers = new double[4];
                double mn = solid.Min(c => c.Diameter), mx = solid.Max(c => c.Diameter);
                for (int k = 0; k < 4; k++) centers[k] = mn + (mx - mn) * k / 3.0;

                int[] assign = new int[solid.Count];
                for (int iter = 0; iter < 50; iter++)
                {
                    for (int i = 0; i < solid.Count; i++)
                    {
                        int best = 0;
                        double bd = double.MaxValue;
                        for (int k = 0; k < 4; k++)
                        {
                            double d = Math.Abs(solid[i].Diameter - centers[k]);
                            if (d < bd) { bd = d; best = k; }
                        }
                        assign[i] = best;
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        double sum = 0; int n = 0;
                        for (int i = 0; i < solid.Count; i++)
                            if (assign[i] == k) { sum += solid[i].Diameter; n++; }
                        if (n > 0) centers[k] = sum / n;
                    }
                }
                
                int[] order = Enumerable.Range(0, 4).OrderBy(k => centers[k]).ToArray();
                for (int i = 0; i < solid.Count; i++)
                    solid[i].Type = 1 + Array.IndexOf(order, assign[i]); 
            }

            int[] counts = new int[5];
            double total = 0;
            foreach (Coin c in coins)
            {
                counts[c.Type]++;
                total += coinValues[c.Type];
            }

            StringBuilder sb = new StringBuilder();
            for (int k = 0; k < 5; k++)
                sb.AppendLine(coinNames[k] + ": " + counts[k]);
            label4.Text = sb.ToString();
            label5.Text = coins.Count.ToString();
            label6.Text = "PHP " + total.ToString("N2");

            processed = new Bitmap(loaded);
            using (Graphics g = Graphics.FromImage(processed))
            {
                string[] shortName = { "5c", "10c", "25c", "P1", "P5" };
                Color[] cols = { Color.Magenta, Color.Blue, Color.Green, Color.Orange, Color.Red };
                foreach (Coin c in coins)
                {
                    using (Pen p = new Pen(cols[c.Type], 2))
                        g.DrawRectangle(p, c.MinX, c.MinY, c.MaxX - c.MinX, c.MaxY - c.MinY);
                    using (Font f = new Font("Arial", Math.Max(8, (float)(c.Diameter * 0.25)), FontStyle.Bold))
                    using (Brush b = new SolidBrush(cols[c.Type]))
                        g.DrawString(shortName[c.Type], f, b, c.MinX, c.MinY - 2);
                }
            }
            pictureBox1.Image = processed;
        }

        private void ShowNone()
        {
            label4.Text = "No coins found";
            label5.Text = "0";
            label6.Text = "PHP 0.00";
        }

        private int[] GetGray(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] buf = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(data);

            int[] gray = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * data.Stride + x * 4; 
                    gray[y * w + x] = (buf[idx] + buf[idx + 1] + buf[idx + 2]) / 3;
                }
            return gray;
        }

        private int OtsuThreshold(int[] gray)
        {
            int[] hist = new int[256];
            foreach (int g in gray) hist[g]++;

            long total = gray.Length;
            double sumAll = 0;
            for (int i = 0; i < 256; i++) sumAll += i * (double)hist[i];

            double sumB = 0, maxVar = 0;
            long wB = 0;
            int threshold = 128;
            for (int i = 0; i < 256; i++)
            {
                wB += hist[i];
                if (wB == 0) continue;
                long wF = total - wB;
                if (wF == 0) break;
                sumB += i * (double)hist[i];
                double mB = sumB / wB;
                double mF = (sumAll - sumB) / wF;
                double between = (double)wB * wF * (mB - mF) * (mB - mF);
                if (between > maxVar) { maxVar = between; threshold = i; }
            }
            return threshold;
        }

        private bool[] FloodOutside(bool[] fg, int w, int h)
        {
            bool[] outside = new bool[w * h];
            int[] stack = new int[w * h];
            int sp = 0;

            for (int x = 0; x < w; x++)
            {
                int a = x, b = (h - 1) * w + x;
                if (!fg[a] && !outside[a]) { outside[a] = true; stack[sp++] = a; }
                if (!fg[b] && !outside[b]) { outside[b] = true; stack[sp++] = b; }
            }
            for (int y = 0; y < h; y++)
            {
                int a = y * w, b = y * w + (w - 1);
                if (!fg[a] && !outside[a]) { outside[a] = true; stack[sp++] = a; }
                if (!fg[b] && !outside[b]) { outside[b] = true; stack[sp++] = b; }
            }

            while (sp > 0)
            {
                int p = stack[--sp];
                int px = p % w, py = p / w;
                if (px > 0) { int n = p - 1; if (!fg[n] && !outside[n]) { outside[n] = true; stack[sp++] = n; } }
                if (px < w - 1) { int n = p + 1; if (!fg[n] && !outside[n]) { outside[n] = true; stack[sp++] = n; } }
                if (py > 0) { int n = p - w; if (!fg[n] && !outside[n]) { outside[n] = true; stack[sp++] = n; } }
                if (py < h - 1) { int n = p + w; if (!fg[n] && !outside[n]) { outside[n] = true; stack[sp++] = n; } }
            }
            return outside;
        }

        private List<Coin> LabelComponents(bool[] mask, int w, int h)
        {
            List<Coin> list = new List<Coin>();
            bool[] visited = new bool[w * h];
            int[] stack = new int[w * h];

            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || visited[start]) continue;

                Coin c = new Coin { MinX = int.MaxValue, MinY = int.MaxValue, MaxX = -1, MaxY = -1 };
                int sp = 0;
                stack[sp++] = start;
                visited[start] = true;

                while (sp > 0)
                {
                    int p = stack[--sp];
                    int px = p % w, py = p / w;
                    c.Area++;
                    if (px < c.MinX) c.MinX = px;
                    if (px > c.MaxX) c.MaxX = px;
                    if (py < c.MinY) c.MinY = py;
                    if (py > c.MaxY) c.MaxY = py;

                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = px + dx, ny = py + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int n = ny * w + nx;
                            if (mask[n] && !visited[n]) { visited[n] = true; stack[sp++] = n; }
                        }
                }
                list.Add(c);
            }
            return list;
        }

        private bool DetectHole(Coin c, bool[] fg, int w, int h)
        {
            int cx = (c.MinX + c.MaxX) / 2;
            int cy = (c.MinY + c.MaxY) / 2;
            int r = Math.Max(2, (int)(c.Diameter * 0.07));
            int bg = 0, total = 0;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r) continue;
                    total++;
                    if (!fg[y * w + x]) bg++;
                }
            return total > 0 && (double)bg / total > 0.8;
        }

        private void label1_Click(object sender, EventArgs e){}

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog1.ShowDialog();
        }

        private void openFileDialog1_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
        {
            loaded = new Bitmap(openFileDialog1.FileName);
            pictureBox1.Image = loaded;
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveFileDialog1.ShowDialog();
        }

        private void label2_Click(object sender, EventArgs e){}

        private void label3_Click(object sender, EventArgs e){}

        private void pixelCopyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loaded == null)
            {
                MessageBox.Show("Please load an image first!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            processed = new Bitmap(loaded.Width, loaded.Height);
            Color pixel;
            for (int x = 0; x < loaded.Width; x++)
            {
                for (int y = 0; y < loaded.Height; y++)
                {
                    pixel = loaded.GetPixel(x, y);
                    processed.SetPixel(x, y, pixel);
                }
            }
            pictureBox1.Image = processed;
        }

        private void grayScalingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loaded == null)
            {
                MessageBox.Show("Please load an image first!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            processed = new Bitmap(loaded.Width, loaded.Height);
            Color pixel;
            int ave;
            for (int x = 0; x < loaded.Width; x++)
            {
                for (int y = 0; y < loaded.Height; y++)
                {
                    pixel = loaded.GetPixel(x, y);
                    ave = (int)(pixel.R + pixel.G + pixel.B) / 3;
                    Color gray = Color.FromArgb(ave, ave, ave);
                    processed.SetPixel(x, y, gray);
                }
            }
            pictureBox1.Image = processed;
        }

        private void inversionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (loaded == null)
            {
                MessageBox.Show("Please load an image first!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            processed = new Bitmap(loaded.Width, loaded.Height); 

            Color pixel;
            for (int x = 0; x < loaded.Width; x++)
            {
                for (int y = 0; y < loaded.Height; y++)
                {
                    pixel = loaded.GetPixel(x, y);
                    Color invert = Color.FromArgb(255 - pixel.R, 255 - pixel.G, 255 - pixel.B);
                    processed.SetPixel(x, y, invert);
                }
            }
            pictureBox1.Image = processed;
        }

        private void label5_Click(object sender, EventArgs e){}

        private void saveFileDialog1_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
        {
            processed.Save(saveFileDialog1.FileName);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Ports;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace hackathonPong
{
    public partial class Form1 : Form
    {
        // Costanti di gioco
        private const int AdcMax = 1023;
        private const int MaxBallSpeed = 20;
        private const int MaxScore = 5;

        // Hardware / I/O
        private readonly SerialPort serialPort1 = new SerialPort();
        private string serialBuffer = string.Empty;
        private readonly object serialLock = new object();
        private int? lastPot1;
        private int? lastPot2;

        // Dimensioni
        private readonly int paddleWidth = 20;
        private readonly int paddleHeight = 100;
        private readonly int ballSize = 20;

        // Stato gioco (layout usa float per maggiore fluidità)
        private Rectangle paddleLeft;
        private Rectangle paddleRight;
        private Rectangle ball;

        private float paddleLeftY;
        private float paddleRightY;

        private int ballSpeedX = 5;
        private int ballSpeedY = 5;

        private int scoreLeft = 0;
        private int scoreRight = 0;

        private bool wPressed;
        private bool sPressed;
        private bool upPressed;
        private bool downPressed;
        private bool isCpuMode;

        private int paddleSpeed = 8;
        private int tempoDiAttesa; // frame di pausa dopo goal

        private readonly Font scoreFont = new Font("Arial", 28, FontStyle.Bold);
        private bool isPaused = false;

        // Skin / colori (modificabili dalla scelta nel menu)
        private Color skinBgTop = Color.FromArgb(18, 18, 30);
        private Color skinBgBottom = Color.FromArgb(6, 6, 20);
        private Color skinPaddle = Color.FromArgb(240, 240, 240);
        private Color skinBall = Color.White;
        private Color skinNet = Color.FromArgb(180, 180, 180);
        private Color skinScore = Color.LightGray;
        private Color skinGlow = Color.White;

        // Impostazioni persistenti
        private GameSettings settings;

        public Form1()
        {
            // Proprietà form
            this.Width = 800;
            this.Height = 600;
            this.BackColor = Color.Black;
            this.Text = "Mini Pong Hackathon";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.DoubleBuffered = true;

            InitializeComponent();
        }

        // Handler sincronizzato (non async): intro è mostrata e quando si chiude il gioco parte subito
        private void Form1_Load(object sender, EventArgs e)
        {
            // Carica impostazioni salvate
            settings = GameSettings.Load();

            // Mostra menu pre-game personalizzato (dark, grande, con design)
            using (var menu = new PreGameMenu(settings))
            {
                if (menu.ShowDialog(this) != DialogResult.OK)
                {
                    this.Close();
                    return;
                }

                // Applica scelta modalità/skin/velocità e (se fornita) porta
                isCpuMode = menu.PlayAgainstCpu;
                ApplySkin(menu.SelectedSkinName);

                // mappa velocità
                switch (menu.GameSpeed)
                {
                    case PreGameMenu.Speed.Low:
                        timer1.Interval = 40;
                        paddleSpeed = 6;
                        ballSpeedX = Math.Sign(ballSpeedX) * 4;
                        ballSpeedY = Math.Sign(ballSpeedY) * 4;
                        break;
                    case PreGameMenu.Speed.Medium:
                        timer1.Interval = 20;
                        paddleSpeed = 8;
                        ballSpeedX = Math.Sign(ballSpeedX) * 5;
                        ballSpeedY = Math.Sign(ballSpeedY) * 5;
                        break;
                    case PreGameMenu.Speed.High:
                        timer1.Interval = 12;
                        paddleSpeed = 12;
                        ballSpeedX = Math.Sign(ballSpeedX) * 7;
                        ballSpeedY = Math.Sign(ballSpeedY) * 7;
                        break;
                }

                // Se l'utente ha fornito una COM valida, prova ad aprire e segnala potenziometri rilevati
                if (!string.IsNullOrWhiteSpace(menu.SelectedComPort))
                {
                    try { OpenSerialPort(menu.SelectedComPort); }
                    catch { MessageBox.Show($"Impossibile aprire {menu.SelectedComPort}. Verrà usata la tastiera.", "Arduino", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }

                // Salva le nuove impostazioni confermate dall'utente
                settings.Skin = menu.SelectedSkinName ?? settings.Skin;
                settings.SpeedIndex = menu.GameSpeed switch
                {
                    PreGameMenu.Speed.Low => 0,
                    PreGameMenu.Speed.High => 2,
                    _ => 1
                };
                settings.ComPort = menu.SelectedComPort ?? string.Empty;
                settings.Save();
            }

            // Modalità schermo intero
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.Bounds = Screen.PrimaryScreen.Bounds;

            InitializeGameObjects();

            // Show intro fullscreen DOPO il menu — ShowDialog blocca finché l'overlay non chiude
            ShowIntro();

            // quando l'overlay si chiude, il gioco parte subito
            timer1.Enabled = true;
        }

        private void ApplySkin(string skinName)
        {
            // Default "Classico"
            switch ((skinName ?? "Classico").ToLowerInvariant())
            {
                case "neon":
                    skinBgTop = Color.FromArgb(8, 0, 20);
                    skinBgBottom = Color.FromArgb(0, 8, 30);
                    skinPaddle = Color.FromArgb(0, 255, 200);
                    skinBall = Color.FromArgb(255, 120, 0);
                    skinNet = Color.FromArgb(0, 255, 180);
                    skinScore = Color.FromArgb(180, 255, 220);
                    skinGlow = Color.FromArgb(0, 255, 200);
                    break;
                case "dark blue":
                case "darkblue":
                    skinBgTop = Color.FromArgb(10, 18, 40);
                    skinBgBottom = Color.FromArgb(2, 8, 30);
                    skinPaddle = Color.FromArgb(200, 230, 255);
                    skinBall = Color.FromArgb(220, 240, 255);
                    skinNet = Color.FromArgb(120, 160, 200);
                    skinScore = Color.FromArgb(200, 220, 255);
                    skinGlow = Color.FromArgb(180, 210, 255);
                    break;
                case "retro":
                    skinBgTop = Color.FromArgb(35, 25, 0);
                    skinBgBottom = Color.FromArgb(10, 10, 0);
                    skinPaddle = Color.FromArgb(255, 200, 80);
                    skinBall = Color.FromArgb(255, 180, 80);
                    skinNet = Color.FromArgb(200, 160, 80);
                    skinScore = Color.FromArgb(255, 210, 140);
                    skinGlow = Color.FromArgb(255, 200, 80);
                    break;
                default: // "classico"
                    skinBgTop = Color.FromArgb(18, 18, 30);
                    skinBgBottom = Color.FromArgb(6, 6, 20);
                    skinPaddle = Color.FromArgb(240, 240, 240);
                    skinBall = Color.White;
                    skinNet = Color.FromArgb(180, 180, 180);
                    skinScore = Color.LightGray;
                    skinGlow = Color.White;
                    break;
            }
        }

        private void InitializeGameObjects()
        {
            int cx = this.ClientSize.Width;
            int cy = this.ClientSize.Height;

            paddleLeft = new Rectangle(50, (cy / 2) - (paddleHeight / 2), paddleWidth, paddleHeight);
            paddleRight = new Rectangle(cx - 70, (cy / 2) - (paddleHeight / 2), paddleWidth, paddleHeight);
            ball = new Rectangle((this.ClientSize.Width / 2) - (ballSize / 2), (this.ClientSize.Height / 2) - (ballSize / 2), ballSize, ballSize);

            // inizializza posizioni float per movimento fluido
            paddleLeftY = paddleLeft.Y;
            paddleRightY = paddleRight.Y;
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DrawGradientBackground(g);
            DrawNet(g);

            // Disegno racchette con angoli arrotondati
            DrawRoundedPaddle(g, paddleLeft, skinPaddle);
            DrawRoundedPaddle(g, paddleRight, skinPaddle);

            // Disegno pallina con "glow"
            DrawBallWithGlow(g, ball, skinBall);

            // Disegno punteggio con ombra
            string leftText = scoreLeft.ToString();
            string rightText = scoreRight.ToString();

            float midX = this.ClientSize.Width / 2f;

            using (var shadowBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            using (var scoreBrush = new SolidBrush(skinScore))
            {
                g.DrawString(leftText, scoreFont, shadowBrush, midX - 120 - 2, 20 + 2);
                g.DrawString(rightText, scoreFont, shadowBrush, midX + 60 - 2, 20 + 2);

                g.DrawString(leftText, scoreFont, scoreBrush, midX - 120, 20);
                g.DrawString(rightText, scoreFont, scoreBrush, midX + 60, 20);
            }

            // Indicazione pausa/space
            string hint = isPaused ? "PAUSA - premi SPACE per riprendere" : "premi SPACE per mettere in pausa";
            using var hintFont = new Font("Arial", 12, FontStyle.Regular);
            var hintSize = g.MeasureString(hint, hintFont);
            g.DrawString(hint, hintFont, Brushes.WhiteSmoke, (this.ClientSize.Width - hintSize.Width) / 2, this.ClientSize.Height - hintSize.Height - 10);
        }

        private void DrawGradientBackground(Graphics g)
        {
            using var lg = new LinearGradientBrush(ClientRectangle, skinBgTop, skinBgBottom, LinearGradientMode.Vertical);
            g.FillRectangle(lg, ClientRectangle);
        }

        private void DrawNet(Graphics g)
        {
            int dashHeight = 20;
            int gap = 10;
            int centerX = this.ClientSize.Width / 2;
            using var pen = new Pen(Color.FromArgb(140, skinNet), 4) { DashPattern = new float[] { dashHeight, gap } };
            g.DrawLine(pen, centerX, 0, centerX, this.ClientSize.Height);
        }

        private void DrawRoundedPaddle(Graphics g, Rectangle paddle, Color color)
        {
            int radius = Math.Max(2, paddle.Width / 2);
            using var path = CreateRoundedRectanglePath(paddle, radius);
            using var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
            var shadowRect = paddle;
            shadowRect.Offset(3, 3);
            using var shadowPath = CreateRoundedRectanglePath(shadowRect, radius);
            g.FillPath(shadowBrush, shadowPath);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }

        private void DrawBallWithGlow(Graphics g, Rectangle ballRect, Color color)
        {
            int glowRadius = ballRect.Width * 3;
            var glowRect = new Rectangle(ballRect.X - (glowRadius - ballRect.Width) / 2, ballRect.Y - (glowRadius - ballRect.Height) / 2, glowRadius, glowRadius);
            using var glowBrush = new SolidBrush(Color.FromArgb(60, skinGlow));
            g.FillEllipse(glowBrush, glowRect);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, ballRect);
            using var pen = new Pen(Color.FromArgb(220, Color.White));
            g.DrawEllipse(pen, ballRect);
        }

        private GraphicsPath CreateRoundedRectanglePath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, radius) * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.W: wPressed = false; break;
                case Keys.S: sPressed = false; break;
                case Keys.Up: upPressed = false; break;
                case Keys.Down: downPressed = false; break;
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                isPaused = !isPaused;
                timer1.Enabled = !isPaused;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.W: wPressed = true; break;
                case Keys.S: sPressed = true; break;
                case Keys.Up: upPressed = true; break;
                case Keys.Down: downPressed = true; break;
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (isPaused) return;

            if (tempoDiAttesa > 0)
            {
                tempoDiAttesa--;
                return;
            }

            int clientH = this.ClientSize.Height;
            int maxPaddleY = clientH - paddleHeight;

            // Lettura input Arduino (se si è aperta la porta in fase di menu)
            var pots = ReadArduinoPots();

            // Se arriva un valore aggiorna immediatamente la sinistra
            if (pots.pot1.HasValue)
            {
                paddleLeftY = ClampFloat((pots.pot1.Value * maxPaddleY) / (float)AdcMax, 0, maxPaddleY);
            }

            // fallback tastiera per la sinistra:
            // se la porta NON è aperta oppure NON ci sono valori Arduino disponibili (né snapshot lastPot)
            if (!serialPort1.IsOpen || (!pots.pot1.HasValue && lastPot1 == null))
            {
                HandleLeftPlayerKeyboard(clientH);
            }

            if (!isCpuMode)
            {
                if (pots.pot2.HasValue)
                {
                    paddleRightY = ClampFloat((pots.pot2.Value * maxPaddleY) / (float)AdcMax, 0, maxPaddleY);
                }
                else
                {
                    // fallback tastiera per destra
                    HandleRightPlayerKeyboard(clientH);
                }
            }
            else
            {
                // CPU: movimento fluido con interpolazione e speed cap
                HandleCpuMovementSmooth(clientH);
            }

            // Aggiorna rect interi da float (render)
            paddleLeft.Y = (int)Math.Round(paddleLeftY);
            paddleRight.Y = (int)Math.Round(paddleRightY);

            // Muovi pallina
            MoveBall();

            // Collisioni e logica punteggio
            HandleCollisions();
            CheckScore();

            this.Invalidate();
        }
        private void OpenSerialPort(string portName)
        {
            try
            {
                if (serialPort1.IsOpen)
                {
                    try { serialPort1.DataReceived -= SerialPort1_DataReceived; } catch { }
                    serialPort1.Close();
                }

                serialPort1.PortName = portName;
                serialPort1.BaudRate = 9600;
                serialPort1.NewLine = "\n";
                serialPort1.ReadTimeout = 300;

                // pulizia stato prima di aprire
                lock (serialLock)
                {
                    serialBuffer = string.Empty;
                    lastPot1 = null;
                    lastPot2 = null;
                }

                // registra handler prima di aprire per non perdere dati
                serialPort1.DataReceived += SerialPort1_DataReceived;
                serialPort1.Open();
            }
            catch
            {
                try { if (serialPort1.IsOpen) { serialPort1.DataReceived -= SerialPort1_DataReceived; serialPort1.Close(); } } catch { }
                throw;
            }
        }

        // Handler event-driven: legge chunk, ricostruisce linee, aggiorna lastPot1/lastPot2 in modo thread-safe
        private void SerialPort1_DataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string chunk = serialPort1.ReadExisting();
                if (string.IsNullOrEmpty(chunk)) return;

                lock (serialLock)
                {
                    serialBuffer += chunk;

                    while (true)
                    {
                        int idx = serialBuffer.IndexOf('\n');
                        if (idx < 0) break;

                        string line = serialBuffer.Substring(0, idx).Trim('\r', '\n', ' ');
                        serialBuffer = serialBuffer.Substring(idx + 1);

                        if (string.IsNullOrWhiteSpace(line)) continue;

                        // parsing flessibile: "512" o "512,123" o "A0:512" etc.
                        var parts = line.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                        var values = new List<int>();
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p, out int v))
                            {
                                values.Add(v);
                            }
                            else
                            {
                                var digits = new string(p.Where(ch => char.IsDigit(ch) || ch == '-').ToArray());
                                if (int.TryParse(digits, out int v2))
                                    values.Add(v2);
                            }

                            if (values.Count >= 2) break;
                        }

                        if (values.Count == 0) continue;

                        lastPot1 = values[0];
                        lastPot2 = values.Count > 1 ? (int?)values[1] : null;
                    }
                }
            }
            catch
            {
                // non interrompere il gioco per errori di parsing/seriale
            }
        }

        // Sostituisce la vecchia ReadArduinoPots — restituisce snapshot (non consuma dati)
        private (int? pot1, int? pot2) ReadArduinoPots()
        {
            lock (serialLock)
            {
                return (lastPot1, lastPot2);
            }
        }

        private void HandleLeftPlayerKeyboard(int clientH)
        {
            if (wPressed && paddleLeftY > 0)
                paddleLeftY = Math.Max(0, paddleLeftY - paddleSpeed);
            if (sPressed && paddleLeftY < clientH - paddleLeft.Height)
                paddleLeftY = Math.Min(clientH - paddleLeft.Height, paddleLeftY + paddleSpeed);
        }

        private void HandleRightPlayerKeyboard(int clientH)
        {
            if (upPressed && paddleRightY > 0)
                paddleRightY = Math.Max(0, paddleRightY - paddleSpeed);
            if (downPressed && paddleRightY < clientH - paddleRight.Height)
                paddleRightY = Math.Min(clientH - paddleRight.Height, paddleRightY + paddleSpeed);
        }

        private void HandleCpuMovementSmooth(int clientH)
        {
            // centro pallina e paddle (float)
            float ballCenterY = ball.Y + (ball.Height / 2f);
            float cpuCenterY = paddleRightY + (paddleRight.Height / 2f);

            if (ballSpeedX > 0)
            {
                float delta = ballCenterY - cpuCenterY;

                // interpolazione proporzionale + cap sulla velocità per evitare scatti
                float k = 0.12f; // fattore smoothing (aumenta per risposta più veloce)
                float desiredMove = delta * k;

                // cap al massimo paddleSpeed per coerenza con tastiera
                float maxMove = paddleSpeed;
                if (desiredMove > maxMove) desiredMove = maxMove;
                if (desiredMove < -maxMove) desiredMove = -maxMove;

                // se delta è piccolo usa spostamento minimo per evitare oscillazioni
                if (Math.Abs(desiredMove) < 0.5f)
                    desiredMove = Math.Sign(desiredMove) * Math.Min(0.5f, Math.Abs(delta));

                paddleRightY += desiredMove;
                paddleRightY = ClampFloat(paddleRightY, 0, clientH - paddleRight.Height);
            }
        }

        private void MoveBall()
        {
            ball.X += ballSpeedX;
            ball.Y += ballSpeedY;

            if (ball.Y < 0)
            {
                ball.Y = 0;
                ballSpeedY = -ballSpeedY;
            }
            else if (ball.Y > this.ClientSize.Height - ball.Height)
            {
                ball.Y = this.ClientSize.Height - ball.Height;
                ballSpeedY = -ballSpeedY;
            }
        }

        private void HandleCollisions()
        {
            bool hitLeft = ball.IntersectsWith(paddleLeft);
            bool hitRight = ball.IntersectsWith(paddleRight);

            if (hitLeft || hitRight)
            {
                var paddle = hitLeft ? paddleLeft : paddleRight;
                int paddleCenter = paddle.Y + paddle.Height / 2;
                int ballCenter = ball.Y + ball.Height / 2;

                double relative = (double)(ballCenter - paddleCenter) / (paddle.Height / 2);
                int maxBounce = 8;
                ballSpeedY = Clamp((int)(relative * maxBounce), -maxBounce, maxBounce);

                ballSpeedX = -ballSpeedX;
                if (Math.Abs(ballSpeedX) < 4) ballSpeedX = Math.Sign(ballSpeedX) * 4;
                if (Math.Abs(ballSpeedX) < MaxBallSpeed) ballSpeedX += Math.Sign(ballSpeedX) * 1;

                try { SystemSounds.Asterisk.Play(); } catch { }
            }
        }

        private void CheckScore()
        {
            if (ball.X < 0)
            {
                scoreRight++;
                try { SystemSounds.Exclamation.Play(); } catch { }
                ResetBall();
                tempoDiAttesa = 50;
            }
            else if (ball.X > this.ClientSize.Width)
            {
                scoreLeft++;
                try { SystemSounds.Exclamation.Play(); } catch { }
                ResetBall();
                tempoDiAttesa = 50;
            }

            if (scoreLeft >= MaxScore)
            {
                timer1.Stop();
                MessageBox.Show("Ha vinto il Giocatore Sinistro!");
                this.Close();
            }
            else if (scoreRight >= MaxScore)
            {
                timer1.Stop();
                MessageBox.Show("Ha vinto il Giocatore Destro!");
                this.Close();
            }
        }

        private void ResetBall()
        {
            ball.X = (this.ClientSize.Width / 2) - (ball.Width / 2);
            ball.Y = (this.ClientSize.Height / 2) - (ball.Height / 2);

            ballSpeedX = ballSpeedX > 0 ? -4 : 4;
            ballSpeedY = ballSpeedY > 0 ? 4 : -4;
        }

        private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
        private static float ClampFloat(float value, float min, float max) => Math.Min(max, Math.Max(min, value));

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (serialPort1.IsOpen)
                serialPort1.Close();

            // salva impostazioni correnti (es. porta eventualmente selezionata)
            try
            {
                if (settings != null)
                {
                    settings.Save();
                }
            }
            catch { /* ignore */ }

            scoreFont.Dispose();
        }

        // --- PRE-GAME MENU ---
        private class PreGameMenu : Form
        {
            public enum Speed { Low, Medium, High }

            public bool PlayAgainstCpu { get; private set; }
            public Speed GameSpeed { get; private set; }
            public string SelectedSkinName { get; private set; }
            public string SelectedComPort { get; private set; } = string.Empty;
            public int DetectedPots { get; private set; } = 0;

            // controls
            private RadioButton rbCpu;
            private RadioButton rbPlayer;
            private ComboBox cbSpeed;
            private ComboBox cbSkin;
            private ComboBox cbComPorts;
            private Button btnDetect;
            private Label lblStatus;
            private Panel previewPanel;
            private Button btnStart;
            private Button btnExit;

            // styling colors
            private readonly Color panelBg = Color.FromArgb(24, 26, 30);
            private readonly Color accent = Color.FromArgb(46, 204, 113);
            private readonly Color foreground = Color.FromArgb(220, 220, 220);

            public PreGameMenu(GameSettings initialSettings)
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.StartPosition = FormStartPosition.CenterParent;
                this.ClientSize = new Size(1000, 600);
                this.BackColor = Color.FromArgb(18, 18, 22);
                this.DoubleBuffered = true;

                InitializeComponents();

                if (initialSettings != null)
                {
                    if (cbSkin.Items.Contains(initialSettings.Skin))
                        cbSkin.SelectedItem = initialSettings.Skin;
                    else
                        cbSkin.SelectedIndex = 0;

                    cbSpeed.SelectedIndex = Math.Clamp(initialSettings.SpeedIndex, 0, 2);

                    if (!string.IsNullOrWhiteSpace(initialSettings.ComPort))
                    {
                        if (!cbComPorts.Items.Contains(initialSettings.ComPort))
                            cbComPorts.Items.Add(initialSettings.ComPort);
                        cbComPorts.SelectedItem = initialSettings.ComPort;
                    }

                    previewPanel?.Invalidate();
                    previewPanel?.Refresh();
                }
            }

            private void InitializeComponents()
            {
                // Title area - smaller
                var lblTitle = new Label()
                {
                    Text = "MINI PONG",
                    Font = new Font("Segoe UI", 20, FontStyle.Bold),
                    ForeColor = accent,
                    Left = 28,
                    Top = 18,
                    AutoSize = true
                };
                this.Controls.Add(lblTitle);

                var lblSubtitle = new Label()
                {
                    Text = "Scegli modalità, velocità, skin e (opz.) Arduino",
                    Font = new Font("Segoe UI", 10, FontStyle.Regular),
                    ForeColor = Color.FromArgb(170, 170, 170),
                    Left = 30,
                    Top = 46,
                    AutoSize = true
                };
                this.Controls.Add(lblSubtitle);

                // Left pane
                var leftPanel = new Panel()
                {
                    Left = 28,
                    Top = 100,
                    Width = 420,
                    Height = 420,
                    BackColor = panelBg,
                    BorderStyle = BorderStyle.None
                };
                leftPanel.Paint += (s, e) =>
                {
                    using var p = new Pen(Color.FromArgb(40, Color.Black));
                    e.Graphics.DrawRectangle(p, 0, 0, leftPanel.Width - 1, leftPanel.Height - 1);
                };
                this.Controls.Add(leftPanel);

                var lblMode = new Label() { Text = "Modalità", Left = 16, Top = 14, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = foreground, AutoSize = true };
                leftPanel.Controls.Add(lblMode);

                rbCpu = new RadioButton() { Text = "Contro CPU", Left = 16, Top = 48, Font = new Font("Segoe UI", 11), ForeColor = foreground, AutoSize = true };
                rbPlayer = new RadioButton() { Text = "2 Giocatori", Left = 16, Top = 78, Font = new Font("Segoe UI", 11), ForeColor = foreground, AutoSize = true };
                rbCpu.Checked = true;
                leftPanel.Controls.AddRange(new Control[] { rbCpu, rbPlayer });

                var lblSpeed = new Label() { Text = "Velocità", Left = 16, Top = 120, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = foreground, AutoSize = true };
                leftPanel.Controls.Add(lblSpeed);
                cbSpeed = new ComboBox() { Left = 16, Top = 150, Width = 180, Font = new Font("Segoe UI", 11), DropDownStyle = ComboBoxStyle.DropDownList };
                cbSpeed.Items.AddRange(new object[] { "Bassa", "Media", "Alta" });
                cbSpeed.SelectedIndex = 1;
                leftPanel.Controls.Add(cbSpeed);

                var lblSkin = new Label() { Text = "Skin", Left = 16, Top = 200, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = foreground, AutoSize = true };
                leftPanel.Controls.Add(lblSkin);
                cbSkin = new ComboBox() { Left = 16, Top = 230, Width = 220, Font = new Font("Segoe UI", 11), DropDownStyle = ComboBoxStyle.DropDownList };
                cbSkin.Items.AddRange(new object[] { "Classico", "Neon", "Dark Blue", "Retro" });
                cbSkin.SelectedIndex = 0;
                cbSkin.SelectedIndexChanged += CbSkin_SelectedIndexChanged;
                leftPanel.Controls.Add(cbSkin);

                var lblCom = new Label() { Text = "COM Arduino (opzionale)", Left = 16, Top = 290, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = foreground, AutoSize = true };
                leftPanel.Controls.Add(lblCom);
                cbComPorts = new ComboBox() { Left = 16, Top = 320, Width = 220, Font = new Font("Segoe UI", 11), DropDownStyle = ComboBoxStyle.DropDownList };
                var ports = SerialPort.GetPortNames().OrderBy(n => n).ToArray();
                if (ports.Length > 0) cbComPorts.Items.AddRange(ports);
                else cbComPorts.Items.Add("Nessuna COM");
                cbComPorts.SelectedIndex = 0;
                leftPanel.Controls.Add(cbComPorts);

                btnDetect = new Button()
                {
                    Text = "Rileva",
                    Left = 250,
                    Top = 320,
                    Width = 120,
                    Height = 34,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(46, 52, 64),
                    ForeColor = foreground,
                    Font = new Font("Segoe UI", 10, FontStyle.Bold)
                };
                btnDetect.FlatAppearance.BorderSize = 0;
                btnDetect.Click += async (s, e) => await DetectArduinoAsync();
                leftPanel.Controls.Add(btnDetect);

                lblStatus = new Label() { Left = 16, Top = 365, Width = 380, Height = 40, Text = "Stato: non verificato", ForeColor = Color.FromArgb(170, 170, 170), AutoSize = false };
                leftPanel.Controls.Add(lblStatus);

                // Right pane preview
                previewPanel = new Panel()
                {
                    Left = 470,
                    Top = 100,
                    Width = 480,
                    Height = 360,
                    BackColor = Color.FromArgb(12, 12, 14),
                    BorderStyle = BorderStyle.None
                };
                EnableDoubleBuffer(previewPanel);
                previewPanel.Paint += PreviewPanel_Paint;
                this.Controls.Add(previewPanel);

                var previewLabel = new Label()
                {
                    Text = "Anteprima Skin",
                    Left = previewPanel.Left,
                    Top = previewPanel.Top - 28,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    ForeColor = foreground,
                    AutoSize = true
                };
                this.Controls.Add(previewLabel);

                // Buttons placed to the right/bottom of preview so not hidden
                btnStart = new Button()
                {
                    Text = "AVVIA GIOCO",
                    Width = 260,
                    Height = 56,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = accent,
                    ForeColor = Color.Black,
                    Font = new Font("Segoe UI", 14, FontStyle.Bold),
                };
                btnStart.FlatAppearance.BorderSize = 0;
                btnStart.Click += (s, e) => { btnStart.Focus(); BtnOk_Click(s, e); };

                btnExit = new Button()
                {
                    Text = "ESCI",
                    Width = 140,
                    Height = 56,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(60, 60, 60),
                    ForeColor = foreground,
                    Font = new Font("Segoe UI", 12, FontStyle.Bold),
                };
                btnExit.FlatAppearance.BorderSize = 0;
                btnExit.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

                btnStart.Left = previewPanel.Right - btnStart.Width - 10;
                btnStart.Top = previewPanel.Bottom + 12;
                btnExit.Left = previewPanel.Right - btnExit.Width - 10;
                btnExit.Top = btnStart.Top + btnStart.Height + 8;

                this.Controls.Add(btnStart);
                this.Controls.Add(btnExit);
                btnStart.BringToFront();
                btnExit.BringToFront();

                this.KeyPreview = true;
                this.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape) this.DialogResult = DialogResult.Cancel;
                };

                this.Paint += (s, e) =>
                {
                    using var p = new Pen(Color.FromArgb(50, 255, 255, 255));
                    e.Graphics.DrawLine(p, 20, 96, this.ClientSize.Width - 20, 96);
                };
            }

            private static void EnableDoubleBuffer(Control c)
            {
                var prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                prop?.SetValue(c, true, null);
            }

            private void CbSkin_SelectedIndexChanged(object sender, EventArgs e)
            {
                previewPanel?.Invalidate();
                previewPanel?.Refresh();
            }

            private void PreviewPanel_Paint(object sender, PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.HighQuality;
                var skin = cbSkin.SelectedItem?.ToString() ?? "Classico";

                Color bgTop, bgBottom, paddle, ball, net, score, glow;
                switch ((skin ?? "Classico").ToLowerInvariant())
                {
                    case "neon":
                        bgTop = Color.FromArgb(5, 2, 18); bgBottom = Color.FromArgb(0, 12, 30);
                        paddle = Color.FromArgb(0, 255, 200); ball = Color.FromArgb(255, 120, 0);
                        net = Color.FromArgb(0, 255, 180); score = Color.FromArgb(180, 255, 220); glow = Color.FromArgb(0, 255, 200);
                        break;
                    case "dark blue":
                    case "darkblue":
                        bgTop = Color.FromArgb(8, 16, 36); bgBottom = Color.FromArgb(2, 8, 30);
                        paddle = Color.FromArgb(200, 230, 255); ball = Color.FromArgb(220, 240, 255);
                        net = Color.FromArgb(120, 160, 200); score = Color.FromArgb(200, 220, 255); glow = Color.FromArgb(180, 210, 255);
                        break;
                    case "retro":
                        bgTop = Color.FromArgb(40, 30, 8); bgBottom = Color.FromArgb(14, 10, 6);
                        paddle = Color.FromArgb(255, 200, 80); ball = Color.FromArgb(255, 180, 80);
                        net = Color.FromArgb(200, 160, 80); score = Color.FromArgb(255, 210, 140); glow = Color.FromArgb(255, 200, 80);
                        break;
                    default:
                        bgTop = Color.FromArgb(18, 18, 30); bgBottom = Color.FromArgb(6, 6, 20);
                        paddle = Color.FromArgb(240, 240, 240); ball = Color.White;
                        net = Color.FromArgb(180, 180, 180); score = Color.LightGray; glow = Color.White;
                        break;
                }

                using var lg = new LinearGradientBrush(previewPanel.ClientRectangle, bgTop, bgBottom, LinearGradientMode.Vertical);
                g.FillRectangle(lg, previewPanel.ClientRectangle);

                using var pen = new Pen(Color.FromArgb(110, net), 3) { DashPattern = new float[] { 10, 6 } };
                g.DrawLine(pen, previewPanel.Width / 2, 8, previewPanel.Width / 2, previewPanel.Height - 8);

                var leftP = new Rectangle(48, previewPanel.Height / 2 - 60, 16, 120);
                var rightP = new Rectangle(previewPanel.Width - 64, previewPanel.Height / 2 - 60, 16, 120);
                var b = new Rectangle(previewPanel.Width / 2 - 12, previewPanel.Height / 2 - 12, 24, 24);

                using var pb = new SolidBrush(paddle);
                FillRoundedRectangle(g, pb, leftP, 8);
                FillRoundedRectangle(g, pb, rightP, 8);

                using var gb = new SolidBrush(Color.FromArgb(90, glow));
                var glowRect = Rectangle.Inflate(b, 36, 36);
                g.FillEllipse(gb, glowRect);

                using var bb = new SolidBrush(ball);
                g.FillEllipse(bb, b);

                using var scoreBrush = new SolidBrush(score);
                using var f = new Font("Segoe UI", 18, FontStyle.Bold);
                var scoreText = "0  :  0";
                var size = g.MeasureString(scoreText, f);
                g.DrawString(scoreText, f, scoreBrush, (previewPanel.Width - size.Width) / 2, 10);
            }

            private void FillRoundedRectangle(Graphics g, Brush b, Rectangle r, int radius)
            {
                using var path = new GraphicsPath();
                int d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(b, path);
            }

            private async Task DetectArduinoAsync()
            {
                lblStatus.Text = "Stato: scansionando porte...";
                lblStatus.Refresh();

                var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
                if (ports.Length == 0)
                {
                    lblStatus.Text = "Stato: nessuna porta COM disponibile.";
                    return;
                }

                string userChoice = cbComPorts.SelectedItem?.ToString();
                string[] toTest = string.IsNullOrWhiteSpace(userChoice) || userChoice == "Nessuna COM"
                    ? ports
                    : new[] { userChoice };

                foreach (var port in toTest)
                {
                    try
                    {
                        using var sp = new SerialPort(port, 9600) { ReadTimeout = 300, NewLine = "\n" };
                        sp.Open();

                        string buffer = "";
                        int attempts = 8; // ~1 second total
                        while (attempts-- > 0)
                        {
                            await Task.Delay(120);
                            try
                            {
                                buffer += sp.ReadExisting();
                            }
                            catch { }

                            if (!string.IsNullOrWhiteSpace(buffer))
                            {
                                var lines = buffer.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                                if (lines.Length > 0)
                                {
                                    var last = lines.Last().Trim();
                                    if (!string.IsNullOrEmpty(last))
                                    {
                                        var parts = last.Split(',').Select(p => p.Trim()).ToArray();
                                        int numericCount = parts.Count(x => int.TryParse(x, out _));
                                        if (numericCount > 0)
                                        {
                                            SelectedComPort = port;
                                            DetectedPots = numericCount;
                                            lblStatus.Text = $"Stato: Arduino trovato su {port} - {numericCount} potenziometro(i) rilevati.";
                                            if (!cbComPorts.Items.Contains(port))
                                            {
                                                cbComPorts.Items.Add(port);
                                            }
                                            cbComPorts.SelectedItem = port;
                                            sp.Close();
                                            return;
                                        }
                                    }
                                }
                            }
                        }

                        sp.Close();
                    }
                    catch
                    {
                        // ignore and try next
                    }
                }

                lblStatus.Text = "Stato: non è stato possibile rilevare Arduino / nessun output valido.";
            }

            // central confirmation handler
            private void BtnOk_Click(object sender, EventArgs e)
            {
                PlayAgainstCpu = rbCpu.Checked;
                GameSpeed = cbSpeed.SelectedIndex switch
                {
                    0 => Speed.Low,
                    2 => Speed.High,
                    _ => Speed.Medium,
                };

                SelectedSkinName = cbSkin.SelectedItem?.ToString() ?? "Classico";

                var chosenCom = cbComPorts.SelectedItem?.ToString();
                SelectedComPort = (chosenCom != null && chosenCom != "Nessuna COM") ? chosenCom : string.Empty;

                // quick probe if user selected a port and detection not run
                if (!string.IsNullOrEmpty(SelectedComPort) && DetectedPots == 0)
                {
                    try
                    {
                        using var sp = new SerialPort(SelectedComPort, 9600) { ReadTimeout = 200 };
                        sp.Open();
                        System.Threading.Thread.Sleep(200);
                        var data = sp.ReadExisting();
                        sp.Close();
                        if (!string.IsNullOrEmpty(data))
                        {
                            var lines = data.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                            if (lines.Length > 0)
                            {
                                var last = lines.Last();
                                var parts = last.Split(',');
                                DetectedPots = parts.Count(p => int.TryParse(p.Trim(), out _));
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                this.DialogResult = DialogResult.OK;
            }
        }

        // --- INTRO OVERLAY ---
        private void ShowIntro()
        {
            using (var intro = new IntroOverlay(this))
            {
                intro.ShowDialog(this);
            }
        }

        private class IntroOverlay : Form
        {
            private readonly System.Windows.Forms.Timer animTimer;
            private int elapsedMs;
            private const int DurationMs = 2000; // 2s
            private float scale = 0.6f;
            private float subtitleAlpha = 0f;
            private readonly Color textColor = Color.FromArgb(46, 204, 113);

            public IntroOverlay(Form owner)
            {
                // full-screen overlay matching owner bounds
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                if (owner != null)
                {
                    this.Bounds = owner.Bounds;
                }
                else
                {
                    this.Bounds = Screen.PrimaryScreen.Bounds;
                }

                BackColor = Color.FromArgb(12, 12, 14);
                DoubleBuffered = true;
                ShowInTaskbar = false;
                Opacity = 1.0;
                TopMost = true;

                animTimer = new System.Windows.Forms.Timer { Interval = 16 };
                animTimer.Tick += AnimTimer_Tick;
                Shown += (s, e) => animTimer.Start();
                Paint += IntroOverlay_Paint;
            }

            // easing helper (smooth out cubic)
            private static float EaseOutCubic(float x) => 1f - (float)Math.Pow(1f - x, 3);

            private void AnimTimer_Tick(object? sender, EventArgs e)
            {
                elapsedMs += animTimer.Interval;
                float t = Math.Min(1f, elapsedMs / (float)DurationMs);
                float eased = EaseOutCubic(t);

                // scale: subtle overshoot then settle
                if (eased < 0.8f) scale = 0.7f + eased * 0.7f;
                else scale = 1.0f + (float)Math.Sin((eased - 0.8f) * Math.PI * 2f) * 0.03f;

                subtitleAlpha = Math.Clamp((t - 0.35f) / 0.65f, 0f, 1f);

                Invalidate();

                if (t >= 1f)
                {
                    animTimer.Stop();
                    // small pause to show final state, then close
                    Task.Delay(250).ContinueWith(_ => { if (!IsDisposed) Invoke((Action)(() => Close())); });
                }
            }

            private void IntroOverlay_Paint(object? sender, PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.HighQuality;

                // modern full-screen gradient
                using var lg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(12, 20, 32), Color.FromArgb(4, 8, 18), LinearGradientMode.Vertical);
                g.FillRectangle(lg, ClientRectangle);

                // animated diagonal lines (soft)
                int lines = 18;
                for (int i = 0; i < lines; i++)
                {
                    float offset = (elapsedMs * 0.08f + i * (ClientSize.Width / (float)lines)) % (ClientSize.Width + 200) - 100;
                    var p1 = new PointF(offset - 200, ClientSize.Height * (i / (float)lines));
                    var p2 = new PointF(offset + 200, ClientSize.Height * (i / (float)lines) + 60);
                    using var pen = new Pen(Color.FromArgb(12 + i % 3 * 6, 255, 255, 255), 2);
                    pen.Alignment = PenAlignment.Center;
                    g.DrawLine(pen, p1, p2);
                }

                // center title
                float cx = ClientSize.Width / 2f;
                float cy = ClientSize.Height / 2f - 24;

                // compute font size relative to screen width for full-screen look
                float baseSize = Math.Clamp(ClientSize.Width / 18f, 36f, 140f);
                using var titleFont = new Font("Segoe UI", baseSize, FontStyle.Bold, GraphicsUnit.Pixel);
                var title = "ArduPong";
                var titleSize = g.MeasureString(title, titleFont);

                // animated circular particles (subtle)
                int particleCount = 12;
                for (int i = 0; i < particleCount; i++)
                {
                    float phase = (elapsedMs / 1000f) * (0.5f + i * 0.03f) + i;
                    float r = 1f + (float)Math.Abs(Math.Sin(phase)) * (ClientSize.Width / 6f) * (i / (float)particleCount);
                    var alpha = 30 + (int)(Math.Abs(Math.Sin(phase * 1.3f)) * 60);
                    using var pb = new SolidBrush(Color.FromArgb(alpha, textColor));
                    g.FillEllipse(pb, cx + (float)Math.Cos(phase) * r - 6, cy + (float)Math.Sin(phase) * r - 6, 12, 12);
                }

                // apply scale animation to title
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(scale, scale);

                // soft glow behind title
                using var glowBrush = new SolidBrush(Color.FromArgb(60, textColor));
                var glowRect = new RectangleF(-titleSize.Width / 2 - 80, -titleSize.Height / 2 - 30, titleSize.Width + 160, titleSize.Height + 60);
                g.FillEllipse(glowBrush, glowRect);

                // drop shadow + main text
                using var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
                using var brush = new SolidBrush(textColor);
                g.DrawString(title, titleFont, shadow, -titleSize.Width / 2 + 8, -titleSize.Height / 2 + 10);
                g.DrawString(title, titleFont, brush, -titleSize.Width / 2, -titleSize.Height / 2);

                g.ResetTransform();

                // subtitle (fade-in)
                using var subFont = new Font("Segoe UI", Math.Max(12, (int)(baseSize / 5)), FontStyle.Regular, GraphicsUnit.Pixel);
                var subtitle = "Powered & Designed By Davit Tadevosyan 2ioT";
                var subSize = g.MeasureString(subtitle, subFont);
                using var subBrush = new SolidBrush(Color.FromArgb((int)(subtitleAlpha * 230), 200, 200, 200));
                g.DrawString(subtitle, subFont, subBrush, cx - subSize.Width / 2, cy + (titleSize.Height / 2) + 16);

                // progress bar bottom
                float progress = Math.Min(1f, elapsedMs / (float)DurationMs);
                using var barBg = new SolidBrush(Color.FromArgb(24, 255, 255, 255));
                using var barFg = new SolidBrush(Color.FromArgb(180, textColor));
                var barRect = new RectangleF(ClientSize.Width * 0.15f, ClientSize.Height - 44, ClientSize.Width * 0.7f, 6);
                g.FillRectangle(barBg, barRect);
                g.FillRectangle(barFg, new RectangleF(barRect.X, barRect.Y, barRect.Width * progress, barRect.Height));
            }

            // removed Arduino icon per richiesta (design semplificato)
            protected override bool ShowWithoutActivation => true;
        }
    }
}
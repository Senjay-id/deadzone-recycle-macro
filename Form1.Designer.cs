using Macro;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Macro
{
    public class Form1 : Form
    {
        // Windows API imports for mouse events
        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        // Mouse event constants
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        // Macro variables
        private bool isMacroRunning = false;
        private bool shouldStopMacro = false;
        private Thread macroThread;

        // Macro configuration
        private Point location1, location2, location3;
        private int loopCount;
        private int delayMs;
        private bool delayInSeconds = false;

        // Timer for mouse position tracking
        private System.Windows.Forms.Timer timerMousePosition;

        // Global keyboard hook
        private LowLevelKeyboardHook keyboardHook;

        // UI Controls
        private Label lblMousePos;
        private TextBox txtLocation1, txtLocation2, txtLocation3;
        private NumericUpDown numLoopCount, numDelay;
        private Button btnStart, btnStop;
        private Label lblStatus;
        private CheckBox chkAlwaysOnTop;
        private CheckBox chkRandomDelay;
        private ComboBox cmbDelayType;
        private Label lblKeybindInfo;

        // INI file path
        private string iniFilePath;

        public Form1()
        {
            InitializeComponent();

            // Set Always on Top to true by default (if not loaded from INI)
            chkAlwaysOnTop.Checked = true;
            this.TopMost = true;

            // Set INI file path in the same directory as the executable
            iniFilePath = Path.Combine(Application.StartupPath, "deadzonerecyclemacro.ini");

            // Load settings from INI file (this will override the default if INI exists)
            LoadSettings();

            // Initialize and start mouse position tracking
            timerMousePosition = new System.Windows.Forms.Timer();
            timerMousePosition.Interval = 100;
            timerMousePosition.Tick += TimerMousePosition_Tick;
            timerMousePosition.Enabled = true;

            // Initialize global keyboard hook
            keyboardHook = new LowLevelKeyboardHook();
            keyboardHook.KeyPressed += OnGlobalKeyPressed;
            keyboardHook.Enable();

            // Also keep form-level key preview for when the form has focus
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
        }

        private void InitializeComponent()
        {
            this.Text = "Dead Zone Recycle Macro by Senjay";
            this.Size = new Size(500, 620);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;

            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                RowCount = 12,
                ColumnCount = 3
            };

            // Set column widths
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

            // Row 0: Mouse position
            mainPanel.Controls.Add(new Label { Text = "Mouse Position:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            lblMousePos = new Label { Text = "X: 0, Y: 0", Dock = DockStyle.Fill };
            mainPanel.Controls.Add(lblMousePos, 1, 0);
            mainPanel.SetColumnSpan(lblMousePos, 2);

            // Row 1-3: Locations
            mainPanel.Controls.Add(new Label { Text = "Item position:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            txtLocation1 = new TextBox { Text = "100,100", Dock = DockStyle.Fill };
            mainPanel.Controls.Add(txtLocation1, 1, 1);
            mainPanel.SetColumnSpan(txtLocation1, 2);

            mainPanel.Controls.Add(new Label { Text = "Recycle button:", TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            txtLocation2 = new TextBox { Text = "200,200", Dock = DockStyle.Fill };
            mainPanel.Controls.Add(txtLocation2, 1, 2);
            mainPanel.SetColumnSpan(txtLocation2, 2);

            mainPanel.Controls.Add(new Label { Text = "Confirm button:", TextAlign = ContentAlignment.MiddleRight }, 0, 3);
            txtLocation3 = new TextBox { Text = "300,300", Dock = DockStyle.Fill };
            mainPanel.Controls.Add(txtLocation3, 1, 3);
            mainPanel.SetColumnSpan(txtLocation3, 2);

            // Row 4: Loop count
            mainPanel.Controls.Add(new Label { Text = "Loop Count:", TextAlign = ContentAlignment.MiddleRight }, 0, 4);
            numLoopCount = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100000,
                Value = 10,
                Dock = DockStyle.Fill
            };
            mainPanel.Controls.Add(numLoopCount, 1, 4);
            mainPanel.SetColumnSpan(numLoopCount, 2);

            // Row 5: Delay
            mainPanel.Controls.Add(new Label { Text = "Delay:", TextAlign = ContentAlignment.MiddleRight }, 0, 5);

            // Delay value - Set a high maximum to avoid conversion issues
            numDelay = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100000, // Allow up to 100000 ms or seconds
                Value = 2000,
                Increment = 100,
                Dock = DockStyle.Fill
            };
            mainPanel.Controls.Add(numDelay, 1, 5);

            // Delay type dropdown
            cmbDelayType = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Items = { "ms", "seconds" }
            };
            cmbDelayType.SelectedIndex = 0; // Default to ms
            cmbDelayType.SelectedIndexChanged += CmbDelayType_SelectedIndexChanged;
            mainPanel.Controls.Add(cmbDelayType, 2, 5);

            // Row 6: Always on Top checkbox
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(0)
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            chkAlwaysOnTop = new CheckBox
            {
                Text = "Always on Top",
                Dock = DockStyle.Fill,
                Checked = true
            };
            chkAlwaysOnTop.CheckedChanged += ChkAlwaysOnTop_CheckedChanged;

            chkRandomDelay = new CheckBox
            {
                Text = "Random Delay (1-1000ms)",
                Dock = DockStyle.Fill,
                Checked = false
            };
            chkRandomDelay.CheckedChanged += ChkRandomDelay_CheckedChanged;

            topPanel.Controls.Add(chkAlwaysOnTop, 0, 0);
            topPanel.Controls.Add(chkRandomDelay, 1, 0);
            mainPanel.Controls.Add(topPanel, 0, 6);
            mainPanel.SetColumnSpan(topPanel, 3);

            // Row 8: Control buttons
            btnStart = new Button { Text = "Start Macro", Dock = DockStyle.Fill };
            btnStart.Click += BtnStart_Click;
            btnStop = new Button { Text = "Stop Macro", Dock = DockStyle.Fill, Enabled = false };
            btnStop.Click += BtnStop_Click;

            var buttonPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            buttonPanel.Controls.Add(btnStart, 0, 0);
            buttonPanel.Controls.Add(btnStop, 1, 0);
            mainPanel.Controls.Add(buttonPanel, 0, 8);
            mainPanel.SetColumnSpan(buttonPanel, 3);

            // Row 9: Status
            lblStatus = new Label
            {
                Text = "Status: Ready",
                Dock = DockStyle.Fill,
                ForeColor = Color.Green
            };
            mainPanel.Controls.Add(lblStatus, 0, 9);
            mainPanel.SetColumnSpan(lblStatus, 3);

            this.Controls.Add(mainPanel);

            // Update delay unit label
            UpdateDelayUnit();
        }

        private void OnGlobalKeyPressed(object sender, Keys key)
        {
            // Check if 'P' key is pressed globally
            if (key == Keys.P)
            {
                // Use Invoke to handle UI updates on the main thread
                this.Invoke((MethodInvoker)delegate {
                    HandlePKeyPress();
                });
            }
            // Check for F1, F2, F3 keys to set mouse positions
            else if (key == Keys.F1)
            {
                this.Invoke((MethodInvoker)delegate {
                    SetCurrentMousePositionToItemPosition();
                });
            }
            else if (key == Keys.F2)
            {
                this.Invoke((MethodInvoker)delegate {
                    SetCurrentMousePositionToRecycleButton();
                });
            }
            else if (key == Keys.F3)
            {
                this.Invoke((MethodInvoker)delegate {
                    SetCurrentMousePositionToConfirmButton();
                });
            }
        }

        private void SetCurrentMousePositionToItemPosition()
        {
            Point mousePos = Cursor.Position;
            txtLocation1.Text = $"{mousePos.X},{mousePos.Y}";
            lblStatus.Text = $"Status: Item position set to X: {mousePos.X}, Y: {mousePos.Y}";
            lblStatus.ForeColor = Color.Blue;

            // Auto-save the settings
            SaveSettings();

            // Reset status after 3 seconds
            ResetStatusAfterDelay();
        }

        private void SetCurrentMousePositionToRecycleButton()
        {
            Point mousePos = Cursor.Position;
            txtLocation2.Text = $"{mousePos.X},{mousePos.Y}";
            lblStatus.Text = $"Status: Recycle button set to X: {mousePos.X}, Y: {mousePos.Y}";
            lblStatus.ForeColor = Color.Blue;

            // Auto-save the settings
            SaveSettings();

            // Reset status after 3 seconds
            ResetStatusAfterDelay();
        }

        private void SetCurrentMousePositionToConfirmButton()
        {
            Point mousePos = Cursor.Position;
            txtLocation3.Text = $"{mousePos.X},{mousePos.Y}";
            lblStatus.Text = $"Status: Confirm button set to X: {mousePos.X}, Y: {mousePos.Y}";
            lblStatus.ForeColor = Color.Blue;

            // Auto-save the settings
            SaveSettings();

            // Reset status after 3 seconds
            ResetStatusAfterDelay();
        }

        private void ResetStatusAfterDelay()
        {
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 3000;
            timer.Tick += (s, ev) =>
            {
                if (!isMacroRunning)
                {
                    lblStatus.Text = "Status: Ready";
                    lblStatus.ForeColor = Color.Green;
                }
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            // Check if 'P' key is pressed when form has focus
            if (e.KeyCode == Keys.P)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                HandlePKeyPress();
            }
            // Check for F1, F2, F3 keys when form has focus
            else if (e.KeyCode == Keys.F1)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SetCurrentMousePositionToItemPosition();
            }
            else if (e.KeyCode == Keys.F2)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SetCurrentMousePositionToRecycleButton();
            }
            else if (e.KeyCode == Keys.F3)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SetCurrentMousePositionToConfirmButton();
            }
        }

        private void HandlePKeyPress()
        {
            if (isMacroRunning)
            {
                // Force stop the macro
                shouldStopMacro = true;
                lblStatus.Text = "Status: Force stopped (P key)";
                lblStatus.ForeColor = Color.Red;

                // Flash the status to indicate force stop
                FlashStatus();

                // Call stop to clean up
                StopMacro();
            }
            else
            {
                // If macro is not running, show a message
                lblStatus.Text = "Status: Macro not running (P key pressed)";
                lblStatus.ForeColor = Color.Gray;

                // Reset status after 2 seconds
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 2000;
                timer.Tick += (s, ev) =>
                {
                    if (!isMacroRunning)
                    {
                        lblStatus.Text = "Status: Ready";
                        lblStatus.ForeColor = Color.Green;
                    }
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }
        }

        private void FlashStatus()
        {
            // Flash the status bar to get user's attention
            var originalColor = lblStatus.ForeColor;
            var flashTimer = new System.Windows.Forms.Timer();
            int flashCount = 0;

            flashTimer.Interval = 200;
            flashTimer.Tick += (s, ev) =>
            {
                flashCount++;
                if (flashCount % 2 == 0)
                {
                    lblStatus.ForeColor = originalColor;
                }
                else
                {
                    lblStatus.ForeColor = Color.Red;
                }

                if (flashCount >= 6) // Flash 3 times
                {
                    flashTimer.Stop();
                    flashTimer.Dispose();
                    if (!isMacroRunning)
                    {
                        lblStatus.ForeColor = Color.Orange;
                    }
                }
            };
            flashTimer.Start();
        }

        private void CmbDelayType_SelectedIndexChanged(object sender, EventArgs e)
        {
            delayInSeconds = cmbDelayType.SelectedIndex == 1;

            // Don't convert the value - just update the increment and let the user adjust
            if (delayInSeconds)
            {
                numDelay.Increment = 1;
                numDelay.Maximum = 100000; // 100000 seconds max
                numDelay.Minimum = 0;
            }
            else
            {
                numDelay.Increment = 100;
                numDelay.Maximum = 100000; // 100000 ms max
                numDelay.Minimum = 0;
            }

            UpdateDelayUnit();
            SaveSettings(); // Save the setting
        }

        private void UpdateDelayUnit()
        {
            // Update the label or tooltip to show the unit
            string unit = delayInSeconds ? "seconds" : "ms";
            numDelay.Tag = unit;
        }

        private void ChkAlwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            this.TopMost = chkAlwaysOnTop.Checked;
            SaveSettings();

            if (chkAlwaysOnTop.Checked)
            {
                lblStatus.Text = "Status: Always on Top - Enabled";
                lblStatus.ForeColor = Color.Blue;
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 2000;
                timer.Tick += (s, ev) =>
                {
                    if (!isMacroRunning)
                    {
                        lblStatus.Text = "Status: Ready";
                        lblStatus.ForeColor = Color.Green;
                    }
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }
            else
            {
                if (!isMacroRunning)
                {
                    lblStatus.Text = "Status: Ready";
                    lblStatus.ForeColor = Color.Green;
                }
            }
        }

        private void ChkRandomDelay_CheckedChanged(object sender, EventArgs e)
        {
            SaveSettings();

            if (chkRandomDelay.Checked)
            {
                lblStatus.Text = "Status: Random Delay - Enabled (Adds 1-1000ms to base delay)";
                lblStatus.ForeColor = Color.Blue;
                // Note: The delay input stays enabled - it's used as the base delay!
            }
            else
            {
                if (!isMacroRunning)
                {
                    lblStatus.Text = "Status: Ready";
                    lblStatus.ForeColor = Color.Green;
                }
            }

            // Reset status after 2 seconds if not running
            if (!isMacroRunning)
            {
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 2000;
                timer.Tick += (s, ev) =>
                {
                    if (!isMacroRunning)
                    {
                        lblStatus.Text = "Status: Ready";
                        lblStatus.ForeColor = Color.Green;
                    }
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }
        }

        private void TimerMousePosition_Tick(object sender, EventArgs e)
        {
            Point mousePos = Cursor.Position;
            lblMousePos.Text = $"X: {mousePos.X}, Y: {mousePos.Y}";
        }

        #region INI File Methods

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(iniFilePath))
                {
                    string[] lines = File.ReadAllLines(iniFilePath);

                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                            continue;

                        string[] parts = line.Split('=');
                        if (parts.Length != 2)
                            continue;

                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        switch (key)
                        {
                            case "Location1":
                                txtLocation1.Text = value;
                                break;
                            case "Location2":
                                txtLocation2.Text = value;
                                break;
                            case "Location3":
                                txtLocation3.Text = value;
                                break;
                            case "LoopCount":
                                if (int.TryParse(value, out int loopVal))
                                    numLoopCount.Value = loopVal;
                                break;
                            case "Delay":
                                if (decimal.TryParse(value, out decimal delayVal))
                                    numDelay.Value = delayVal;
                                break;
                            case "DelayType":
                                if (int.TryParse(value, out int delayTypeVal))
                                {
                                    cmbDelayType.SelectedIndex = delayTypeVal;
                                    delayInSeconds = delayTypeVal == 1;

                                    // Update increment based on the loaded setting
                                    if (delayInSeconds)
                                    {
                                        numDelay.Increment = 1;
                                        numDelay.Maximum = 100000;
                                    }
                                    else
                                    {
                                        numDelay.Increment = 100;
                                        numDelay.Maximum = 100000;
                                    }
                                    numDelay.Minimum = 0;
                                }
                                break;
                            case "AlwaysOnTop":
                                if (bool.TryParse(value, out bool topMost))
                                {
                                    chkAlwaysOnTop.Checked = topMost;
                                    this.TopMost = topMost;
                                }
                                break;
                            case "RandomDelay":
                                if (bool.TryParse(value, out bool randomDelay))
                                {
                                    chkRandomDelay.Checked = randomDelay;
                                }
                                break;
                        }
                    }

                    // Ensure delay type is properly set
                    if (cmbDelayType.SelectedIndex == -1)
                        cmbDelayType.SelectedIndex = 0;

                    UpdateDelayUnit();
                    lblStatus.Text = "Status: Settings loaded";
                    lblStatus.ForeColor = Color.Green;
                }
                else
                {
                    // Create default INI file if it doesn't exist
                    SaveSettings();
                    lblStatus.Text = "Status: Default settings created";
                    lblStatus.ForeColor = Color.Green;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Status: Error loading settings: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
        }

        private void SaveSettings()
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendLine("; Automatically generated config");
                sb.AppendLine();

                sb.AppendLine($"Location1={txtLocation1.Text}");
                sb.AppendLine($"Location2={txtLocation2.Text}");
                sb.AppendLine($"Location3={txtLocation3.Text}");
                sb.AppendLine($"LoopCount={numLoopCount.Value}");
                sb.AppendLine($"Delay={numDelay.Value}");
                sb.AppendLine($"DelayType={cmbDelayType.SelectedIndex}");
                sb.AppendLine($"AlwaysOnTop={chkAlwaysOnTop.Checked}");
                sb.AppendLine($"RandomDelay={chkRandomDelay.Checked}");

                File.WriteAllText(iniFilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        private void BtnStart_Click(object sender, EventArgs e)
        {
            try
            {
                string[] parts1 = txtLocation1.Text.Split(',');
                string[] parts2 = txtLocation2.Text.Split(',');
                string[] parts3 = txtLocation3.Text.Split(',');

                location1 = new Point(int.Parse(parts1[0].Trim()), int.Parse(parts1[1].Trim()));
                location2 = new Point(int.Parse(parts2[0].Trim()), int.Parse(parts2[1].Trim()));
                location3 = new Point(int.Parse(parts3[0].Trim()), int.Parse(parts3[1].Trim()));
                loopCount = (int)numLoopCount.Value;

                // Get delay in milliseconds
                if (delayInSeconds)
                {
                    // If in seconds, convert to milliseconds
                    delayMs = (int)(numDelay.Value * 1000);
                }
                else
                {
                    delayMs = (int)numDelay.Value;
                }

                if (loopCount == 0)
                {
                    MessageBox.Show("Loop count must be greater than 0!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Save settings before starting
                SaveSettings();
                StartMacro();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid input: {ex.Message}\nPlease use format: X,Y", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartMacro()
        {
            isMacroRunning = true;
            shouldStopMacro = false;

            btnStart.Enabled = false;
            btnStop.Enabled = true;
            lblStatus.Text = "Status: Running (Press 'P' key to stop)";
            lblStatus.ForeColor = Color.Red;

            macroThread = new Thread(ExecuteMacro);
            macroThread.IsBackground = true;
            macroThread.Start();
        }

        private void ExecuteMacro()
        {
            try
            {
                Random random = new Random();  // Random number generator

                for (int i = 0; i < loopCount && !shouldStopMacro; i++)
                {
                    if (shouldStopMacro) break;

                    // Location 1 - Click
                    this.Invoke((MethodInvoker)delegate {
                        SetCursorPos(location1.X, location1.Y);
                    });
                    Thread.Sleep(50);
                    this.Invoke((MethodInvoker)delegate {
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    });
                    Thread.Sleep(50);
                    this.Invoke((MethodInvoker)delegate {
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    });
                    Thread.Sleep(100);

                    if (shouldStopMacro) break;

                    // Location 2 - Click
                    this.Invoke((MethodInvoker)delegate {
                        SetCursorPos(location2.X, location2.Y);
                    });
                    Thread.Sleep(50);
                    this.Invoke((MethodInvoker)delegate {
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    });
                    Thread.Sleep(50);
                    this.Invoke((MethodInvoker)delegate {
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    });
                    Thread.Sleep(100);

                    if (shouldStopMacro) break;

                    // Location 3 - Click
                    this.Invoke((MethodInvoker)delegate {
                        SetCursorPos(location3.X, location3.Y);
                    });
                    Thread.Sleep(50);
                    this.Invoke((MethodInvoker)delegate {
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    });
                    Thread.Sleep(50);
                    this.Invoke((MethodInvoker)delegate {
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    });
                    Thread.Sleep(100);

                    // Calculate base delay
                    int baseDelayMs;
                    string baseDelayDisplay;

                    if (delayInSeconds)
                    {
                        baseDelayMs = (int)(numDelay.Value * 1000);
                        baseDelayDisplay = $"{numDelay.Value}s";
                    }
                    else
                    {
                        baseDelayMs = (int)numDelay.Value;
                        baseDelayDisplay = $"{numDelay.Value}ms";
                    }

                    // Calculate total delay
                    int totalDelayMs = baseDelayMs;
                    string totalDelayDisplay = baseDelayDisplay;

                    if (chkRandomDelay.Checked)
                    {
                        // Generate random delay between 1-1000ms and ADD it to the base delay
                        int randomAddMs = random.Next(1, 1001);
                        totalDelayMs = baseDelayMs + randomAddMs;
                        totalDelayDisplay = $"{baseDelayDisplay} + {randomAddMs}ms = {totalDelayMs}ms total";
                    }

                    // Update status with delay info
                    this.Invoke((MethodInvoker)delegate {
                        if (chkRandomDelay.Checked)
                        {
                            // Show the combined delay value in the status
                            lblStatus.Text = $"Status: Running - Loop {i + 1}/{loopCount} | Total Delay: {totalDelayDisplay} | Press 'P' to stop";
                            lblStatus.ForeColor = Color.Orange;  // Orange to highlight random mode
                        }
                        else
                        {
                            lblStatus.Text = $"Status: Running - Loop {i + 1}/{loopCount} (Delay: {baseDelayDisplay}) | Press 'P' to stop";
                            lblStatus.ForeColor = Color.Red;
                        }
                    });

                    // Delay between loops using the TOTAL delay
                    if (i < loopCount - 1 && !shouldStopMacro)
                    {
                        Thread.Sleep(totalDelayMs);
                    }
                }

                this.Invoke((MethodInvoker)delegate {
                    if (shouldStopMacro)
                    {
                        lblStatus.Text = "Status: Stopped";
                        lblStatus.ForeColor = Color.Red;
                    }
                    else
                    {
                        if (chkRandomDelay.Checked)
                        {
                            lblStatus.Text = "Status: Macro completed with random delays added!";
                            lblStatus.ForeColor = Color.Green;
                        }
                        else
                        {
                            lblStatus.Text = "Status: Macro completed!";
                            lblStatus.ForeColor = Color.Green;
                        }
                    }
                    StopMacro();
                });
            }
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate {
                    MessageBox.Show($"Error in macro: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    StopMacro();
                });
            }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            shouldStopMacro = true;
            StopMacro();
        }

        private void StopMacro()
        {
            isMacroRunning = false;
            shouldStopMacro = true;

            btnStart.Enabled = true;
            btnStop.Enabled = false;

            if (lblStatus.Text != "Status: Stopped" &&
                lblStatus.Text != "Status: Force stopped (P key)")
            {
                lblStatus.Text = "Status: Stopped";
                lblStatus.ForeColor = Color.Orange;
            }

            if (macroThread != null && macroThread.IsAlive)
            {
                if (!macroThread.Join(2000))
                {
                    macroThread.IsBackground = true;
                }
                macroThread = null;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Disable keyboard hook
            keyboardHook?.Disable();
            keyboardHook?.Dispose();

            SaveSettings();
            timerMousePosition?.Stop();
            timerMousePosition?.Dispose();
            shouldStopMacro = true;
            StopMacro();
            base.OnFormClosing(e);
        }
    }
}
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using System.Net;
using System.Net.Sockets;
using System.Data;

namespace PhoenixHeadTracker
{

    public partial class Form1 : Form
    {
        [DllImport("AirAPI_Windows", CallingConvention = CallingConvention.Cdecl)]
        public static extern int StartConnection();

        [DllImport("AirAPI_Windows", CallingConvention = CallingConvention.Cdecl)]
        public static extern int StopConnection();

        [DllImport("AirAPI_Windows", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetEuler();

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern uint SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion inputUnion;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mouseInput;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;

        }

        private Bitmap image1;
        private Graphics graphics1;
        private PointF center1;
        private Bitmap image2;
        private Graphics graphics2;
        private PointF center2;
        private Bitmap image3;
        private Graphics graphics3;
        private PointF center3;

        private bool isMouseTrack = false;
        private bool isOpenTrack = false;

        private double previousX = 0;
        private double previousY = 0;
        private double previousRoll = 0;
        private double xMapped = 0;
        private double yMapped = 0;
        private double rollMapped = 0;
        private double deltaX = 0;
        private double deltaY = 0;
        private double deltaRoll = 0;
        private double xRot = 0.0;
        private double yRot = 0.0;
        private double rollRot = 0.0;

        private double rotDistanceYaw = 0.0;
        private double rotDistancePitch = 0.0;
        private double rollDistanceRoll = 0.0;
        private double rotStartYaw = 0.0;
        private double rotStartPitch = 0.0;
        private double rotStartRoll = 0.0;

        private const int WM_INPUT = 0x00FF;
        private const int MOUSEEVENTF_MOVE = 0x0001;

        private int screenWidth = 1920;
        private int screenWidthScale = 1920;
        private int screenHeightScale = 1920;
        private int screenRollScale = 1920;

        private double x = 0;
        private double y = 0;
        private double roll = 0;

        private INPUT input;

        private IntPtr hWnd;

        private IPAddress ipAddress;
        private IPEndPoint endPoint;
        private int port;

        private IntPtr ptr;
        private float[] arr;

        private int FightDriftX=0;
        private int FightDriftY = 0;
        private int FightDriftRoll = 0;

        double t = 0.01;
        double r = 0.002;
        double p = 1.0;

        KalmanFilter filterx;
        double filteredValuex;
        KalmanFilter filtery;
        double filteredValuey;
        KalmanFilter filterr;
        double filteredValuer;

        private double driftXCompensation = 0;
        private double driftYCompensation = 0;
        private double driftRollCompensation = 0;
        private double lowPassFilterAlpha = 0.1;
        private double lastXRot = 0;
        private double lastYRot = 0;
        private double lastRollRot = 0;
        private int updateCount = 0;
        private void Form1_Load(object sender, EventArgs e)
        {

            screenWidth = Screen.PrimaryScreen.Bounds.Width;

            screenWidthScale = screenWidth;
            screenHeightScale = screenWidth;
            screenRollScale = screenWidth;
            textBoxYawTrackValue.Text= screenWidthScale.ToString();
            textBoxPitchTrackValue.Text= screenHeightScale.ToString();
            textBoxRollTrackValue.Text= screenRollScale.ToString();

            input = new INPUT();
            input.type = 0;
            input.inputUnion.mouseInput.mouseData = 0;
            input.inputUnion.mouseInput.dwFlags = MOUSEEVENTF_MOVE;
            input.inputUnion.mouseInput.time = 0;
            input.inputUnion.mouseInput.dwExtraInfo = GetMessageExtraInfo();

        }

        private void timer1_Tick(object sender, EventArgs e)
        {

            ptr = GetEuler();
            arr = new float[3];

            Marshal.Copy(ptr, arr, 0, 3);

            x = (arr[2] * (screenWidthScale * screenWidthScale) / trackBarYawSpeed.Value);
            y = (arr[1] * (screenHeightScale * screenHeightScale) / trackBarPitchSpeed.Value);
            roll = (arr[0] * (screenRollScale * screenRollScale) / trackBarRollSpeed.Value);

            xMapped = x;
            yMapped = y;
            rollMapped = roll;

            labelYawSpeed.Text = string.Format("Yaw Speed: {0:0}", trackBarYawSpeed.Value);
            labelPitchSpeed.Text = string.Format("Pitch Speed: {0:0}", trackBarPitchSpeed.Value);
            labelRollSpeed.Text = string.Format("Roll Speed: {0:0}", trackBarRollSpeed.Value);

            if (xMapped != previousX || yMapped != previousY || rollMapped != previousRoll)
            {

                if (Math.Abs(xMapped - previousX)<6000 )
                {
                    deltaX = xMapped - previousX;
                }
                if (Math.Abs(yMapped - previousY) < 6000)
                {
                    deltaY = yMapped - previousY;
                }
                if (Math.Abs(rollMapped - previousRoll) < 6000)
                {
                    deltaRoll = rollMapped - previousRoll;
                }

                filterx = new KalmanFilter(t, r, p, deltaX);
                filteredValuex = filterx.Update(deltaX);
                filtery = new KalmanFilter(t, r, p, deltaY);
                filteredValuey = filtery.Update(deltaY);
                filterr = new KalmanFilter(t, r, p, deltaRoll);
                filteredValuer = filterr.Update(deltaRoll);

                xRot += (double)filteredValuex ;
                yRot += (double)filteredValuey ;
                rollRot += (double)filteredValuer ;

                double deltaXRotation = arr[2] - lastXRot;
                double deltaYRotation = arr[1] - lastYRot;
                double deltaRollRotation = arr[0] - lastRollRot;

                driftXCompensation += deltaXRotation * 0.01;
                driftYCompensation += deltaYRotation * 0.01;
                driftRollCompensation += deltaRollRotation * 0.01;

                double tempX2 = (xRot - arr[2]);
                xRot += (rotDistanceYaw - tempX2) - driftXCompensation-FightDriftX;

                double tempY2 = (yRot - arr[1]);
                yRot += rotDistancePitch - tempY2 - driftYCompensation - FightDriftY; ;

                double tempRoll2 = (rollRot - arr[0]);
                rollRot += rollDistanceRoll - tempRoll2 - driftRollCompensation - FightDriftRoll; ;

                textBoxLog1.Text = string.Format("driftCompensation {0}\r\n", driftXCompensation);

                lastXRot = arr[2];
                lastYRot = arr[1];
                lastRollRot = arr[0];

                updateCount++;
                labelDriftFilter.Text = string.Format("Mouse Speed : {0:0}", trackBarDrift.Value);

                labelYawRotation.Text = xRot.ToString();
                labelPitchRotation.Text = yRot.ToString();
                labelRollRotation.Text = rollRot.ToString();

                previousX = xMapped;
                previousY = yMapped;
                previousRoll = rollMapped;

                int mouseSmoothFilter = trackBarMouseSmooth.Value;
                labelMouseSmoothFilter.Text = string.Format("Mouse Smooth Filter: {0:0}", mouseSmoothFilter);
                int mouseDelayFilter = trackBarMouseDelay.Value;
                labelMouseDelayFilter.Text = string.Format("Mouse Delay Filter : {0:0}", mouseDelayFilter);

                labelMatchYaw.Text = string.Format("Track Yaw {0:0.00}", xRot);
                labelMatchPitch.Text = string.Format("Track Pitch {0:0.00}", yRot);
                labelMatchRoll.Text = string.Format("Track Roll {0:0.00}", rollRot);

                if (isOpenTrack == true)
                {

                    double x = 0;
                    double y = 0;
                    double z = 0;
                    double yaw = xRot;
                    double pitch = yRot;
                    double roll = rollRot;

                    xRot = yaw;
                    yRot = pitch;
                    rollRot = roll;

                    labelMatchYaw.Text = string.Format("Track Yaw {0:0.00}", yaw);
                    labelMatchPitch.Text = string.Format("Track Pitch {0:0.00}", pitch);
                    labelMatchRoll.Text = string.Format("Track Roll {0:0.00}", roll);

                    if (checkBoxInvertYaw.Checked == true) yaw = -yaw;
                    if (checkBoxInvertPitch.Checked == true) pitch = -pitch;
                    if (checkBoxInvertRoll.Checked == true) roll = -roll;

                    if (checkBoxYaw.Checked == false) { yaw = 0; }
                    if (checkBoxPitch.Checked == false) { pitch = 0; }
                    if (checkBoxRoll.Checked == false) { roll = 0; }

                    using (UdpClient udpClient = new UdpClient())
                    {

                        byte[] data = new byte[6 * sizeof(double)];
                        Buffer.BlockCopy(new double[] { x, y, z, yaw, pitch, roll }, 0, data, 0, data.Length);

                        udpClient.Send(data, data.Length, endPoint);

                    }

                }

                if (isMouseTrack == true)
                {

                    for (int q = 0; q < mouseSmoothFilter; q++)
                    {

                        input.inputUnion.mouseInput.dx = -(int)(filteredValuex * (0.02 * trackBarDrift.Value));
                        input.inputUnion.mouseInput.dy = (int)(filteredValuey * (0.02 * trackBarDrift.Value));

                        IntPtr lParam = Marshal.AllocHGlobal(Marshal.SizeOf(input));

                        Marshal.StructureToPtr(input, lParam, false);

                        uint result = SendMessage(hWnd, WM_INPUT, IntPtr.Zero, lParam);

                        Marshal.FreeHGlobal(lParam);

                        System.Threading.Thread.Sleep(mouseDelayFilter);
                    }
                }

            }

            graphics1.Clear(Color.White);
            graphics2.Clear(Color.White);
            graphics3.Clear(Color.White);

            graphics1.TranslateTransform(center1.X, center1.Y);
            graphics2.TranslateTransform(center2.X, center2.Y);
            graphics3.TranslateTransform(center3.X, center3.Y);

            graphics1.RotateTransform((float)-xRot);
            graphics2.RotateTransform((float)-yRot);
            graphics3.RotateTransform((float)rollRot);

            graphics1.DrawImage(Properties.Resources.imageYaw, -center1.X, -center1.Y);
            graphics2.DrawImage(Properties.Resources.imagePitch, -center2.X, -center2.Y);
            graphics3.DrawImage(Properties.Resources.imageRoll, -center3.X, -center3.Y);

            pictureBox1.Image = image1;
            pictureBox2.Image = image2;
            pictureBox3.Image = image3;

            graphics1.ResetTransform();
            graphics2.ResetTransform();
            graphics3.ResetTransform();

            labelRawYaw.Text = string.Format("Raw Yaw: {0:0.00}", arr[2]);
            labelRawPitch.Text = string.Format("Raw Pitch: {0:0.00}", arr[1]);
            labelRawRoll.Text = string.Format("Raw Roll: {0:0.00}", arr[0]);

            textBoxMouseLocation.Text = string.Format("Mouse X:{0} Mouse Y:{1}", Cursor.Position.X, Cursor.Position.Y);
        }

        private void timer2_Tick(object sender, EventArgs e)
        {

            isMouseTrack = false;
            isOpenTrack = false;
            buttonReset.Enabled = true;
            trackBarYawSpeed.Enabled = true;
            trackBarPitchSpeed.Enabled = true;
            trackBarRollSpeed.Enabled = true;
            trackBarDrift.Enabled = true;
            labelDriftFilter.Enabled = true;
            groupBoxOpentrack.Enabled = true;
            groupBoxMouseTrack.Enabled = true;

            ResetValues();

            (sender as Timer).Stop();

        }

        private void ResetValues()
        {

            if (buttonStartOpentrack.Enabled==false)
            {
                xRot = 0;
                yRot = 0;
                rollRot = 0;
            }
            else
            {
                xRot = 0;
                yRot = 0;
                rollRot = 0;
            }

            deltaX = 0;
            deltaY = 0;
            deltaRoll = 0;

            rotStartYaw = arr[2];
            rotStartPitch = arr[1];
            rotStartRoll = arr[0];

            rotDistanceYaw = xRot - arr[2];
            rotDistancePitch = yRot - arr[1];
            rollDistanceRoll = rollRot - arr[0];

        }

        private void buttonStartOpentrack_Click(object sender, EventArgs e)
        {

            isMouseTrack = false;
            isOpenTrack = true;
            buttonStopOpentrack.Enabled = true;
            buttonStartOpentrack.Enabled = false;
            textBoxIPAddress.Enabled = false;
            textBoxPort.Enabled = false;
            groupBoxMouseTrack.Enabled = false;
            trackBarYawSpeed.Enabled = false;
            trackBarPitchSpeed.Enabled = false;
            trackBarRollSpeed.Enabled = false;

            labelFightDriftX.Enabled = true;
            labelFightDriftY.Enabled = true;
            labelFightDriftRoll.Enabled = true;
            buttonFightDriftXMinus.Enabled = true;
            buttonFightDriftXPlus.Enabled = true;
            buttonFightDriftYMinus.Enabled = true;
            buttonFightDriftYPlus.Enabled = true;
            buttonFightDriftRollMinus.Enabled = true;
            buttonFightDriftRollPlus.Enabled = true;
            trackBarDrift.Enabled = false;
            labelDriftFilter.Enabled = false;

            ResetValues();
            ipAddress = IPAddress.Parse(textBoxIPAddress.Text);
            port = int.Parse(textBoxPort.Text);

            endPoint = new IPEndPoint(ipAddress, port);
            screenWidthScale = int.Parse(textBoxYawTrackValue.Text);
            screenHeightScale = int.Parse(textBoxPitchTrackValue.Text);
            screenRollScale = int.Parse(textBoxRollTrackValue.Text);

            textBoxYawTrackValue.Enabled = false;
            textBoxPitchTrackValue.Enabled = false;
            textBoxRollTrackValue.Enabled = false;
        }

        private void buttonStopOpentrack_Click(object sender, EventArgs e)
        {

            isMouseTrack = false;
            isOpenTrack = false;
            buttonStopOpentrack.Enabled = false;
            buttonStartOpentrack.Enabled = true;
            textBoxIPAddress.Enabled = true;
            textBoxPort.Enabled = true;
            groupBoxMouseTrack.Enabled = true;
            trackBarYawSpeed.Enabled = true;
            trackBarPitchSpeed.Enabled = true;
            trackBarRollSpeed.Enabled = true;

            labelFightDriftX.Enabled = false;
            labelFightDriftY.Enabled = false;
            labelFightDriftRoll.Enabled = false;
            buttonFightDriftXMinus.Enabled = false;
            buttonFightDriftXPlus.Enabled = false;
            buttonFightDriftYMinus.Enabled = false;
            buttonFightDriftYPlus.Enabled = false;
            buttonFightDriftRollMinus.Enabled = false;
            buttonFightDriftRollPlus.Enabled = false;

            trackBarDrift.Enabled = true;
            labelDriftFilter.Enabled = true;

            textBoxYawTrackValue.Enabled = true;
            textBoxPitchTrackValue.Enabled = true;
            textBoxRollTrackValue.Enabled = true;
        }

        private void buttonReset_Click(object sender, EventArgs e)
        {
            ResetValues();
        }

        protected override void WndProc(ref Message m)
        {

            if (m.Msg == WM_INPUT)
            {

                if (m.LParam != null)
                {

                    INPUT raw = (INPUT)Marshal.PtrToStructure(m.LParam, typeof(INPUT));

                    try
                    {

                        if (isMouseTrack)
                        {

                            if (SendInput(1, new INPUT[] { raw }, Marshal.SizeOf(raw)) == 0)
                            {

                            }
                        }
                    }
                    finally
                    {

                    }
                }
            }

            base.WndProc(ref m);
        }

        private void button2_Click(object sender, EventArgs e)
        {

            int result = StartConnection();

            if (result == 1)
            {

                label2.Text = "Connected";

                hWnd = base.Handle;

                Timer timer = new Timer();
                timer.Interval = 6000;

                timer.Tick += new EventHandler(timer2_Tick);

                timer.Start();
            }
            else
            {

                label2.Text = "Not Connected";
            }

            timer1.Enabled = true;
        }

        private void buttonMouseTrackOn_Click(object sender, EventArgs e)
        {

            isMouseTrack = true;
            isOpenTrack = false;
            buttonMouseTrackOn.Enabled = false;
            buttonMouseTrackOff.Enabled = true;
            trackBarYawSpeed.Enabled = false;
            trackBarPitchSpeed.Enabled = false;
            trackBarRollSpeed.Enabled = false;
            groupBoxOpentrack.Enabled = false;

            screenWidthScale = int.Parse(textBoxYawTrackValue.Text);
            screenHeightScale= int.Parse(textBoxPitchTrackValue.Text);
            screenRollScale= int.Parse(textBoxRollTrackValue.Text);

            textBoxYawTrackValue.Enabled = false;
            textBoxPitchTrackValue.Enabled = false;
            textBoxRollTrackValue.Enabled = false;
        }

        private void buttonMouseTrackOff_Click(object sender, EventArgs e)
        {
            isMouseTrack = false;
            isOpenTrack = false;
            buttonMouseTrackOn.Enabled = true;
            buttonMouseTrackOff.Enabled = false;
            trackBarYawSpeed.Enabled = true;
            trackBarPitchSpeed.Enabled = true;
            trackBarRollSpeed.Enabled = true;
            groupBoxOpentrack.Enabled = true;

            textBoxYawTrackValue.Enabled = true;
            textBoxPitchTrackValue.Enabled = true;
            textBoxRollTrackValue.Enabled = true;
        }

        public Form1()
        {

            InitializeComponent();

            image1 = new Bitmap(pictureBox1.Width, pictureBox1.Height);

            graphics1 = Graphics.FromImage(image1);

            center1 = new PointF(pictureBox1.Width / 2f, pictureBox1.Height / 2f);

            image2 = new Bitmap(pictureBox2.Width, pictureBox2.Height);

            graphics2 = Graphics.FromImage(image2);

            center2 = new PointF(pictureBox2.Width / 2f, pictureBox2.Height / 2f);

            image3 = new Bitmap(pictureBox3.Width, pictureBox3.Height);

            graphics3 = Graphics.FromImage(image3);

            center3 = new PointF(pictureBox3.Width / 2f, pictureBox3.Height / 2f);

            timer1.Enabled = false;
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                VisitLink();
            }
            catch (Exception)
            {
                MessageBox.Show("Unable to open link that was clicked.");
            }
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                VisitLink2();
            }
            catch (Exception)
            {
                MessageBox.Show("Unable to open link that was clicked.");
            }
        }

        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                VisitLink3();
            }
            catch (Exception)
            {
                MessageBox.Show("Unable to open link that was clicked.");
            }
        }

        private void VisitLink()
        {
            linkLabel1.LinkVisited = true;
            Process.Start("https://github.com/iVideoGameBoss/PhoenixHeadTracker");
        }

        private void VisitLink2()
        {
            linkLabel1.LinkVisited = true;
            Process.Start("https://www.youtube.com/@ivideogameboss");
        }

        private void VisitLink3()
        {
            linkLabel1.LinkVisited = true;
            Process.Start("https://www.buymeacoffee.com/ivideogameboss");
        }

        private void buttonFightDriftXPlus_Click(object sender, EventArgs e)
        {
            FightDriftX += 1;
            labelFightDriftX.Text = string.Format("Opentrack Fight Drift {0:0}", FightDriftX.ToString());
        }

        private void buttonFightDriftXMinus_Click(object sender, EventArgs e)
        {
            FightDriftX -= 1;
            labelFightDriftX.Text = string.Format("Opentrack Fight Drift {0:0}", FightDriftX.ToString());
        }

        private void buttonFightDriftYMinus_Click(object sender, EventArgs e)
        {
            FightDriftY -= 1;
            labelFightDriftY.Text = string.Format("Opentrack Fight Drift {0:0}", FightDriftY.ToString());
        }

        private void buttonFightDriftYPlus_Click(object sender, EventArgs e)
        {
            FightDriftY += 1;
            labelFightDriftY.Text = string.Format("Opentrack Fight Drift {0:0}", FightDriftY.ToString());
        }

        private void buttonFightDriftRollMinus_Click(object sender, EventArgs e)
        {
            FightDriftRoll -= 1;
            labelFightDriftRoll.Text = string.Format("Opentrack Fight Drift {0:0}", FightDriftRoll.ToString());
        }

        private void buttonFightDriftRollPlus_Click(object sender, EventArgs e)
        {
            FightDriftRoll += 1;
            labelFightDriftRoll.Text = string.Format("Opentrack Fight Drift {0:0}", FightDriftRoll.ToString());
        }

    }

    public class KalmanFilter
    {
        private double _q;
        private double _r;
        private double _p;
        private double _x;

        public KalmanFilter(double q, double r, double p, double x)
        {
            _q = q;
            _r = r;
            _p = p;
            _x = x;
        }

        public double Update(double measurement)
        {

            _p += _q;

            double k = _p / (_p + _r);
            _x += k * (measurement - _x);
            _p *= (1 - k);

            return _x;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PhoenixAirViewer.Core;
using PhoenixAirViewer.Platform;

namespace PhoenixAirViewer.Tests
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && string.Equals(args[0], "--capture-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCaptureProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--capture-all-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCaptureAllProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--renderer-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunRendererProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--camera-convention-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunCameraConventionProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--visual-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunVisualProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--renderer-screen-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunRendererScreenProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--live-hardware-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunLiveHardwareProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--stationary-pose-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunStationaryPoseProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--session-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunSessionProbe();
                }

                TestNormalizeRejectsZero();
                TestDefaultAirSensorMapping();
                TestAxisSensitivity();
                TestDriftRateIntegration();
                TestWorldLockedCameraConvention();
                TestPresentationTransformModes();
                TestPanelCurvatureDirection();
                TestIndependentPanelCurvature();
                TestMixedAxisRelativePoseComposition();
                TestDriftRateIntegration();
                TestDistanceProfiles();
                TestDefaultPoseSettingsAreLowLatency();
                TestPoseStabilityGuard();
                TestFirstSampleRecenters();
                TestAutoRecenterWaitsForStartup();
                TestRecenterProducesIdentity();
                TestSmoothingUsesTimeConstant();
                TestMaximumAngularVelocity();
                TestHorizonLockRemovesRoll();
                TestRollLockRemovesZTwist();
                TestPanelSettingsValidation();
                TestWideCurvedMonitorDefaults();
                TestViewerSettingsPersistence();
                TestViewerSettingsMigratesDefaultAirMapping();
                TestHotkeySettings();
                TestViewerLogging();
                TestLatestPoseStore();
                TestLatestPoseObservationStore();
                TestPosePresentationSnapshot();
                TestPoseEvidenceTargetsAndSerialization();
                TestPoseCalibration();
                TestPoseCalibrationRotatedBasis();
                TestRenderSchedulerRetainsStaticFrame();
                TestPosePollingWorkerPublishesLatest();
                TestPosePollingWorkerReconnectsAfterFailures();
                TestPosePollingWorkerCanStopBeforeStart();
                Console.WriteLine("All PhoenixAirViewer core tests passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.ToString());
                return 1;
            }
        }

        private static int RunCaptureProbe()
        {
            var displays = DisplayEnumerator.Enumerate();
            if (displays.Count == 0)
            {
                throw new InvalidOperationException("No Windows displays were enumerated.");
            }

            using (DesktopDuplicationCapture capture = new DesktopDuplicationCapture(displays[0]))
            {
                DesktopCaptureResult result = capture.TryAcquire(100);
                Console.WriteLine("Display: " + displays[0]);
                Console.WriteLine("Capture status: " + result.Status);
                if (result.Frame != null)
                {
                    Console.WriteLine("Frame: " + result.Frame.Width + "x" + result.Frame.Height);
                    uint packedBgra;
                    if (capture.TryReadLatestPixel(result.Frame.Width / 2, result.Frame.Height / 2, out packedBgra))
                    {
                        Console.WriteLine("Center pixel BGRA=0x" + packedBgra.ToString("X8"));
                    }

                    uint topLeft;
                    uint topRight;
                    uint bottomLeft;
                    uint bottomRight;
                    if (capture.TryReadLatestPixel(0, 0, out topLeft) &&
                        capture.TryReadLatestPixel(result.Frame.Width - 1, 0, out topRight) &&
                        capture.TryReadLatestPixel(0, result.Frame.Height - 1, out bottomLeft) &&
                        capture.TryReadLatestPixel(result.Frame.Width - 1, result.Frame.Height - 1, out bottomRight))
                    {
                        Console.WriteLine(
                            "Corner pixels BGRA=0x{0:X8},0x{1:X8},0x{2:X8},0x{3:X8}",
                            topLeft,
                            topRight,
                            bottomLeft,
                            bottomRight);
                    }
                }

                if (result.Status == DesktopCaptureStatus.Error || result.Status == DesktopCaptureStatus.AccessLost || result.Status == DesktopCaptureStatus.DeviceRemoved)
                {
                    Console.Error.WriteLine(result.Error);
                    return 1;
                }
            }

            return 0;
        }

        private static int RunRendererProbe()
        {
            var displays = DisplayEnumerator.Enumerate();
            if (displays.Count == 0)
            {
                throw new InvalidOperationException("No Windows displays were enumerated.");
            }

            using (DesktopDuplicationCapture capture = new DesktopDuplicationCapture(displays[0]))
            using (Form window = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, ClientSize = new System.Drawing.Size(1280, 720) })
            {
                DesktopCaptureResult result = capture.TryAcquire(100);
                if (result.Status != DesktopCaptureStatus.FrameReady)
                {
                    Console.WriteLine("Renderer probe skipped because capture returned " + result.Status + ".");
                    return result.Status == DesktopCaptureStatus.Timeout ? 0 : 1;
                }

                using (D3D11PanelRenderer renderer = new D3D11PanelRenderer(capture, window.Handle, 1280, 720))
                {
                    renderer.Render(result.Frame);
                    window.ClientSize = new System.Drawing.Size(1024, 768);
                    renderer.Render(result.Frame);
                    renderer.UpdatePanelSettings(new PanelSettings
                    {
                        PanelWidthMeters = 2.0f,
                        PanelHeightMeters = 1.125f,
                        PanelDistanceMeters = 2.5f,
                        CurvatureRadiusMeters = 2.5f
                    });
                    renderer.Render(result.Frame, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(8.0f)));
                }

                Console.WriteLine("Renderer probe presented flat and curved " + result.Frame.Width + "x" + result.Frame.Height + " desktop frames.");
            }

            return 0;
        }

        private static int RunCameraConventionProbe()
        {
            TestWorldLockedCameraConvention();
            TestMixedAxisRelativePoseComposition();
            Console.WriteLine("Camera convention probe passed: inverse world lock and mixed-axis pose composition are consistent.");
            return 0;
        }

        private static int RunCaptureAllProbe()
        {
            var displays = DisplayEnumerator.Enumerate();
            for (int index = 0; index < displays.Count; index++)
            {
                DisplayInfo display = displays[index];
                try
                {
                    using (DesktopDuplicationCapture capture = new DesktopDuplicationCapture(display))
                    {
                        DesktopCaptureResult result = capture.TryAcquire(100);
                        Console.WriteLine("Display: " + display + "; status=" + result.Status);
                        if (result.Frame != null)
                        {
                            uint centerPixel = 0;
                            uint topLeft = 0;
                            uint topRight = 0;
                            uint bottomLeft = 0;
                            uint bottomRight = 0;
                            bool hasPixel = capture.TryReadLatestPixel(result.Frame.Width / 2, result.Frame.Height / 2, out centerPixel) &&
                                capture.TryReadLatestPixel(0, 0, out topLeft) &&
                                capture.TryReadLatestPixel(result.Frame.Width - 1, 0, out topRight) &&
                                capture.TryReadLatestPixel(0, result.Frame.Height - 1, out bottomLeft) &&
                                capture.TryReadLatestPixel(result.Frame.Width - 1, result.Frame.Height - 1, out bottomRight);
                            Console.WriteLine(
                                "Frame: " + result.Frame.Width + "x" + result.Frame.Height +
                                "; pixels=" + (hasPixel
                                    ? string.Format("0x{0:X8},0x{1:X8},0x{2:X8},0x{3:X8},0x{4:X8}", topLeft, topRight, centerPixel, bottomLeft, bottomRight)
                                    : "unavailable") +
                                "; " + capture.DescribeLatestTexture());
                        }
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine("Display: " + display.DeviceName + "; error=" + exception.Message);
                }
            }

            return 0;
        }

        private static int RunSessionProbe()
        {
            var displays = DisplayEnumerator.Enumerate();
            if (displays.Count == 0)
            {
                throw new InvalidOperationException("No Windows displays were enumerated.");
            }

            if (displays.Count < 2)
            {
                Console.WriteLine("Session probe skipped because it requires separate source and output displays.");
                return 0;
            }

            using (Form window = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, ClientSize = new System.Drawing.Size(1280, 720) })
            using (ManualResetEventSlim statusReceived = new ManualResetEventSlim(false))
            using (DesktopViewerSession session = new DesktopViewerSession(displays[0], displays[1], window.Handle, 1280, 720, null, null))
            {
                string status = null;
                session.StatusChanged += delegate(string value)
                {
                    status = value;
                    Console.WriteLine("Session: " + value);
                    statusReceived.Set();
                };
                session.Start();
                statusReceived.Wait(TimeSpan.FromSeconds(3));
                if (status == null || status.StartsWith("stopped", StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }
            }

            Console.WriteLine("Session probe stopped cleanly.");
            return 0;
        }

        private static int RunVisualProbe()
        {
            var displays = DisplayEnumerator.Enumerate();
            if (displays.Count < 2)
            {
                Console.WriteLine("Visual probe skipped because it requires separate source and output displays.");
                return 0;
            }

            DisplayInfo sourceDisplay = displays[1];
            DisplayInfo outputDisplay = displays[0];
            ViewerSettings settings = ViewerSettingsStore.CreateDefault().Load();
            for (int index = 0; index < displays.Count; index++)
            {
                if (string.Equals(displays[index].DeviceName, settings.SourceDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    sourceDisplay = displays[index];
                }

                if (string.Equals(displays[index].DeviceName, settings.OutputDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    outputDisplay = displays[index];
                }
            }

            using (DesktopDuplicationCapture capture = new DesktopDuplicationCapture(sourceDisplay))
            using (Form window = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                Bounds = outputDisplay.Bounds,
                KeyPreview = true
            })
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 })
            using (D3D11PanelRenderer renderer = new D3D11PanelRenderer(
                capture,
                window.Handle,
                (uint)Math.Max(1, window.ClientSize.Width),
                (uint)Math.Max(1, window.ClientSize.Height),
                settings.Panel))
            {
                DesktopCaptureFrame latestFrame = null;
                bool reportedPixel = false;
                timer.Tick += delegate
                {
                    DesktopCaptureResult result = capture.TryAcquire(16);
                    if (result.Status == DesktopCaptureStatus.FrameReady)
                    {
                        latestFrame = result.Frame;
                        if (!reportedPixel && capture.TryReadLatestPixel(result.Frame.Width / 2, result.Frame.Height / 2, out uint packedBgra))
                        {
                            Console.WriteLine("Visual probe center pixel BGRA=0x" + packedBgra.ToString("X8"));
                            reportedPixel = true;
                        }
                    }

                    if (latestFrame != null)
                    {
                        renderer.Render(latestFrame, Quaternion.Identity);
                    }
                };
                window.KeyDown += delegate(object sender, KeyEventArgs args)
                {
                    if (args.KeyCode == Keys.Escape)
                    {
                        window.Close();
                    }
                };
                window.Shown += delegate
                {
                    Console.WriteLine("Visual probe: " + sourceDisplay.DeviceName + " -> " + outputDisplay.DeviceName + ". Press Escape to stop.");
                    timer.Start();
                };
                window.FormClosed += delegate
                {
                    timer.Stop();
                };
                Application.Run(window);
            }

            return 0;
        }

        private static int RunRendererScreenProbe()
        {
            var displays = DisplayEnumerator.Enumerate();
            if (displays.Count == 0)
            {
                Console.WriteLine("Renderer screen probe skipped because no display is available.");
                return 0;
            }

            DisplayInfo display = displays[0];
            using (DesktopDuplicationCapture capture = new DesktopDuplicationCapture(display))
            {
                DesktopCaptureResult result = capture.TryAcquire(100);
                if (result.Status != DesktopCaptureStatus.FrameReady)
                {
                    Console.WriteLine("Renderer screen probe skipped because capture returned " + result.Status + ".");
                    return result.Status == DesktopCaptureStatus.Timeout ? 0 : 1;
                }

                using (Form window = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    ShowInTaskbar = false,
                    TopMost = true,
                    Bounds = display.Bounds
                })
                using (D3D11PanelRenderer renderer = new D3D11PanelRenderer(
                    capture,
                    window.Handle,
                    (uint)Math.Max(1, window.ClientSize.Width),
                    (uint)Math.Max(1, window.ClientSize.Height)))
                using (System.Windows.Forms.Timer screenshotTimer = new System.Windows.Forms.Timer { Interval = 250 })
                {
                    screenshotTimer.Tick += delegate
                    {
                        screenshotTimer.Stop();
                        renderer.Render(result.Frame, Quaternion.Identity);
                        string path = SaveDisplayScreenshot(display.Bounds, "renderer-screen");
                        Console.WriteLine("Renderer screen probe screenshot=" + path);
                        window.Close();
                    };
                    window.Shown += delegate
                    {
                        renderer.Render(result.Frame, Quaternion.Identity);
                        screenshotTimer.Start();
                    };
                    Application.Run(window);
                }
            }

            return 0;
        }

        private static int RunLiveHardwareProbe()
        {
            ViewerSettings settings = ViewerSettingsStore.CreateDefault().Load();
            IViewerLogger logger;
            try
            {
                logger = FileViewerLogger.CreateDefault();
            }
            catch
            {
                logger = NullViewerLogger.Instance;
            }

            using (logger)
            using (AirPoseSource poseSource = new AirPoseSource(AirQuaternionLayout.Wxyz, logger))
            using (LatestPoseWorkerScope poseScope = new LatestPoseWorkerScope(poseSource, logger))
            {
                Console.WriteLine("Hardware probe: starting Air sensor connection on the STA probe thread before selecting displays.");
                string connectionError;
                if (!poseSource.TryConnect(out connectionError))
                {
                    Console.Error.WriteLine("Hardware probe failed: " + (connectionError ?? "Air connection failed."));
                    return 2;
                }

                poseScope.Worker.Start();
                PoseSample sample;
                if (!WaitForFreshPose(poseScope.Store, 8000, out sample))
                {
                    Console.Error.WriteLine("Hardware probe failed: Air did not produce a fresh quaternion sample within 8 seconds.");
                    return 2;
                }

                IList<DisplayInfo> displays = DisplayEnumerator.Enumerate();
                Console.WriteLine("Hardware probe: Air pose sample received; displays=" + displays.Count + ".");
                for (int index = 0; index < displays.Count; index++)
                {
                    Console.WriteLine("  " + displays[index]);
                }

                DisplayInfo outputDisplay;
                string detectionReason;
                if (!DisplayEnumerator.TryFindXrealDisplay(displays, out outputDisplay, out detectionReason))
                {
                    Console.Error.WriteLine("Hardware probe failed: " + detectionReason);
                    return 3;
                }

                DisplayInfo sourceDisplay = FindDesktopSource(displays, outputDisplay);
                if (sourceDisplay == null)
                {
                    Console.Error.WriteLine("Hardware probe failed: no separate desktop source display is available.");
                    return 3;
                }

                Console.WriteLine("Hardware probe: XREAL output=" + outputDisplay.DeviceName + "; source=" + sourceDisplay.DeviceName + ".");
                Console.WriteLine("Hardware probe: detection=" + detectionReason);
                Console.WriteLine("Hardware probe: starting the real DesktopViewerSession for 15 seconds or until Escape.");

                bool presented = false;
                bool screenshotSaved = false;
                string screenshotPath = null;
                string sourceScreenshotPath = null;
                DateTime presentationTime = DateTime.MinValue;
                using (Form outputWindow = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    ShowInTaskbar = false,
                    TopMost = true,
                    Bounds = outputDisplay.Bounds,
                    BackColor = Color.Black,
                    KeyPreview = true
                })
                using (DesktopViewerSession session = new DesktopViewerSession(
                    sourceDisplay,
                    outputDisplay,
                    outputWindow.Handle,
                    (uint)Math.Max(1, outputWindow.ClientSize.Width),
                    (uint)Math.Max(1, outputWindow.ClientSize.Height),
                    poseSource,
                    new PosePipeline(settings.Pose),
                    settings.Panel,
                    logger,
                    poseScope.Store))
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 })
                {
                    session.StatusChanged += delegate(string status)
                    {
                        Console.WriteLine("Hardware probe session: " + status);
                        if (status != null && status.StartsWith("presenting", StringComparison.OrdinalIgnoreCase))
                        {
                            presented = true;
                            presentationTime = DateTime.UtcNow;
                        }

                        if (status != null && status.StartsWith("stopped", StringComparison.OrdinalIgnoreCase) && outputWindow.IsHandleCreated)
                        {
                            try
                            {
                                outputWindow.BeginInvoke(new Action(outputWindow.Close));
                            }
                            catch (InvalidOperationException)
                            {
                            }
                        }
                    };
                    outputWindow.Shown += delegate
                    {
                        session.Start();
                        timer.Start();
                    };
                    outputWindow.KeyDown += delegate(object sender, KeyEventArgs args)
                    {
                        if (args.KeyCode == Keys.Escape)
                        {
                            outputWindow.Close();
                        }
                    };
                    outputWindow.FormClosed += delegate
                    {
                        timer.Stop();
                    };
                    timer.Tick += delegate
                    {
                        if (presented && !screenshotSaved && (DateTime.UtcNow - presentationTime).TotalSeconds >= 2.0)
                        {
                            screenshotPath = SaveDisplayScreenshot(outputDisplay.Bounds, "output");
                            screenshotSaved = true;
                            Console.WriteLine("Hardware probe: live output screenshot=" + screenshotPath);
                            sourceScreenshotPath = SaveDisplayScreenshot(sourceDisplay.Bounds, "source");
                            Console.WriteLine("Hardware probe: live source screenshot=" + sourceScreenshotPath);
                        }

                        if (presented && (DateTime.UtcNow - presentationTime).TotalSeconds >= 15.0)
                        {
                            outputWindow.Close();
                        }
                    };
                    Application.Run(outputWindow);
                }

                bool stopped = poseScope.Worker.Stop(3000);
                if (!stopped)
                {
                    Console.Error.WriteLine("Hardware probe warning: pose worker did not stop within 3 seconds.");
                }

                if (!presented)
                {
                    Console.Error.WriteLine("Hardware probe failed: the real session never reported a presented frame.");
                    return 4;
                }

                Console.WriteLine("Hardware probe passed: Air connected, XREAL output auto-detected, live session presented, screenshot captured=" + screenshotSaved + ".");
                return 0;
            }
        }

        private static int RunStationaryPoseProbe()
        {
            ViewerSettings settings = ViewerSettingsStore.CreateDefault().Load();
            IViewerLogger logger;
            try
            {
                logger = FileViewerLogger.CreateDefault();
            }
            catch
            {
                logger = NullViewerLogger.Instance;
            }

            string outputPath = Path.Combine(
                Path.GetTempPath(),
                "PhoenixAirViewer-stationary-pose-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".csv");
            using (logger)
            using (AirPoseSource poseSource = new AirPoseSource(AirQuaternionLayout.Wxyz, logger))
            using (StreamWriter writer = new StreamWriter(outputPath, false))
            {
                string connectionError;
                if (!poseSource.TryConnect(out connectionError))
                {
                    Console.Error.WriteLine("Stationary pose probe failed: " + (connectionError ?? "Air connection failed."));
                    return 2;
                }

                PosePipelineSettings pipelineSettings = settings.Pose.Clone();
                pipelineSettings.AutoRecenterOnFirstSample = false;
                pipelineSettings.AutoRecenterDelaySeconds = 0.0f;
                pipelineSettings.YawDriftRateDegreesPerSecond = 0.0f;
                pipelineSettings.PitchDriftRateDegreesPerSecond = 0.0f;
                pipelineSettings.Validate();
                PosePipeline pipeline = new PosePipeline(pipelineSettings);
                Quaternion reference = Quaternion.Identity;
                Quaternion previousMapped = Quaternion.Identity;
                bool hasReference = false;
                bool hasPreviousMapped = false;
                double minimumRelativeAngle = double.MaxValue;
                double maximumRelativeAngle = 0.0;
                double maximumStepAngle = 0.0;
                int sampleCount = 0;
                int failedReadCount = 0;
                long startTicks = PoseClock.NowTicks();
                long nextReportTicks = startTicks;
                long deadlineTicks = startTicks + Stopwatch.Frequency * 30;

                writer.AutoFlush = true;
                writer.WriteLine("utc,elapsedSeconds,native0,native1,native2,native3,decodedX,decodedY,decodedZ,decodedW,mappedX,mappedY,mappedZ,mappedW,relativeX,relativeY,relativeZ,relativeW,relativeAngleDegrees,relativeRotationXDegrees,relativeRotationYDegrees,relativeRotationZDegrees,processedX,processedY,processedZ,processedW,sampleAgeMilliseconds");
                Console.WriteLine("Stationary pose probe: connected. Keep the glasses face down and still for 30 seconds.");
                Console.WriteLine("Stationary pose probe: compensation disabled; output=" + outputPath);

                while (PoseClock.NowTicks() < deadlineTicks)
                {
                    PoseObservation observation;
                    if (poseSource.TryGetLatestObservation(out observation))
                    {
                        Quaternion mapped = PoseMath.MapBasis(observation.Orientation, pipelineSettings.SensorToRenderer);
                        if (!hasReference)
                        {
                            reference = mapped;
                            pipeline.Recenter(observation.Sample);
                            hasReference = true;
                        }

                        Quaternion relative = PoseMath.Normalize(Quaternion.Multiply(Quaternion.Inverse(reference), mapped));
                        Quaternion processed;
                        pipeline.TryProcess(observation.Sample, out processed);
                        Vector3 relativeRotation = PoseMath.ToRotationVector(relative) * (180.0f / (float)Math.PI);
                        double relativeAngle = PoseMath.AngularDistanceRadians(Quaternion.Identity, relative) * 180.0 / Math.PI;
                        double stepAngle = hasPreviousMapped
                            ? PoseMath.AngularDistanceRadians(previousMapped, mapped) * 180.0 / Math.PI
                            : 0.0;
                        minimumRelativeAngle = Math.Min(minimumRelativeAngle, relativeAngle);
                        maximumRelativeAngle = Math.Max(maximumRelativeAngle, relativeAngle);
                        maximumStepAngle = Math.Max(maximumStepAngle, stepAngle);
                        previousMapped = mapped;
                        hasPreviousMapped = true;
                        sampleCount++;

                        writer.WriteLine(string.Join(",", new string[]
                        {
                            DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                            PoseClock.SecondsBetween(startTicks, PoseClock.NowTicks()).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                            observation.NativeComponents.X.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            observation.NativeComponents.Y.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            observation.NativeComponents.Z.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            observation.NativeComponents.W.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            observation.Orientation.X.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            observation.Orientation.Y.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            observation.Orientation.Z.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            observation.Orientation.W.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            mapped.X.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            mapped.Y.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            mapped.Z.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            mapped.W.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            relative.X.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            relative.Y.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            relative.Z.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            relative.W.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            relativeAngle.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                            relativeRotation.X.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                            relativeRotation.Y.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                            relativeRotation.Z.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                            processed.X.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            processed.Y.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            processed.Z.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            processed.W.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture),
                            (observation.Sample.AgeSeconds(PoseClock.NowTicks()) * 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        }));

                        long nowTicks = PoseClock.NowTicks();
                        if (nowTicks >= nextReportTicks)
                        {
                            Console.WriteLine(
                                "t=" + PoseClock.SecondsBetween(startTicks, nowTicks).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                                "s; relative=" + relativeAngle.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                                "deg; relativeRotation=" + relativeRotation.X.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                                "," + relativeRotation.Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                                "," + relativeRotation.Z.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                                "deg; step=" + stepAngle.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                                "deg; age=" + (observation.Sample.AgeSeconds(nowTicks) * 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "ms");
                            nextReportTicks = nowTicks + Stopwatch.Frequency;
                        }
                    }
                    else
                    {
                        failedReadCount++;
                    }

                    Thread.Sleep(100);
                }

                Console.WriteLine(
                    "Stationary pose probe complete: samples=" + sampleCount +
                    "; failedReads=" + failedReadCount +
                    "; relativeRange=" + (sampleCount == 0 ? "n/a" : minimumRelativeAngle.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".." + maximumRelativeAngle.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " deg") +
                    "; maximumStep=" + maximumStepAngle.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " deg; csv=" + outputPath);
                return sampleCount > 0 ? 0 : 4;
            }
        }

        private static bool WaitForFreshPose(LatestPoseStore store, int timeoutMilliseconds, out PoseSample sample)
        {
            long deadline = PoseClock.NowTicks() + (long)(timeoutMilliseconds * Stopwatch.Frequency / 1000.0);
            while (PoseClock.NowTicks() < deadline)
            {
                if (store.TryRead(out sample) && sample.AgeSeconds(PoseClock.NowTicks()) <= 0.5)
                {
                    return true;
                }

                Thread.Sleep(25);
            }

            sample = default(PoseSample);
            return false;
        }

        private static DisplayInfo FindDesktopSource(IList<DisplayInfo> displays, DisplayInfo outputDisplay)
        {
            for (int index = 0; index < displays.Count; index++)
            {
                if (displays[index].IsPrimary && !string.Equals(displays[index].DeviceName, outputDisplay.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return displays[index];
                }
            }

            for (int index = 0; index < displays.Count; index++)
            {
                if (!string.Equals(displays[index].DeviceName, outputDisplay.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return displays[index];
                }
            }

            return null;
        }

        private static string SaveDisplayScreenshot(Rectangle bounds, string role)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
            "PhoenixAirViewer-live-" + role + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            using (Bitmap bitmap = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            return path;
        }

        private sealed class LatestPoseWorkerScope : IDisposable
        {
            public LatestPoseWorkerScope(AirPoseSource source, IViewerLogger logger)
            {
                Store = new LatestPoseStore();
                Worker = new PosePollingWorker(source, Store, logger, false);
            }

            public LatestPoseStore Store { get; private set; }
            public PosePollingWorker Worker { get; private set; }

            public void Dispose()
            {
                Worker.Dispose();
            }
        }

        private static void TestNormalizeRejectsZero()
        {
            Quaternion normalized;
            AssertFalse(PoseMath.TryNormalize(new Quaternion(0, 0, 0, 0), out normalized), "zero quaternion should be rejected");
        }

        private static void TestDefaultAirSensorMapping()
        {
            PosePipelineSettings settings = new PosePipelineSettings();
            AssertVectorNear(-Vector3.UnitX, Vector3.Transform(Vector3.UnitX, settings.SensorToRenderer), "Air pitch axis should map to renderer pitch");
            AssertVectorNear(-Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, settings.SensorToRenderer), "Air roll axis should map to renderer roll");
            AssertVectorNear(-Vector3.UnitY, Vector3.Transform(Vector3.UnitZ, settings.SensorToRenderer), "Air yaw axis should map to renderer yaw");
        }

        private static void TestAxisSensitivity()
        {
            PosePipelineSettings defaults = new PosePipelineSettings();
            AssertTrue(defaults.PitchSensitivity == PosePipelineSettings.DefaultPitchSensitivity, "default pitch sensitivity should preserve the known-good direction");
            AssertTrue(defaults.YawSensitivity == PosePipelineSettings.DefaultYawSensitivity, "default yaw sensitivity should reverse the observed direction");
            AssertTrue(defaults.RollSensitivity == PosePipelineSettings.DefaultRollSensitivity, "default roll sensitivity should reverse the observed direction");

            Quaternion pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, Degrees(20.0f));
            Quaternion halfPitch = PoseMath.ApplyAxisSensitivity(pitch, 0.5f, 1.0f, 1.0f);
            AssertTrue(
                PoseMath.AngularDistanceRadians(halfPitch, Quaternion.CreateFromAxisAngle(Vector3.UnitX, Degrees(10.0f))) < Degrees(0.1f),
                "pitch sensitivity should scale the renderer pitch angle");

            Quaternion yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(20.0f));
            Quaternion reversedYaw = PoseMath.ApplyAxisSensitivity(yaw, 1.0f, -1.0f, 1.0f);
            AssertTrue(
                PoseMath.AngularDistanceRadians(reversedYaw, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(-20.0f))) < Degrees(0.1f),
                "negative yaw sensitivity should reverse the renderer yaw direction");

            Quaternion roll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Degrees(20.0f));
            Quaternion reversedRoll = PoseMath.ApplyAxisSensitivity(roll, 1.0f, 1.0f, -1.0f);
            AssertTrue(
                PoseMath.AngularDistanceRadians(reversedRoll, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Degrees(-20.0f))) < Degrees(0.1f),
                "negative roll sensitivity should reverse the renderer roll direction");

            Quaternion disabledYaw = PoseMath.ApplyAxisSensitivity(yaw, 1.0f, 0.0f, 1.0f);
            AssertAngleNear(0.0f, disabledYaw, "zero yaw sensitivity should suppress a pure yaw rotation");

            PosePipelineSettings cloneSource = new PosePipelineSettings
            {
                PitchSensitivity = 0.75f,
                YawSensitivity = -1.25f,
                RollSensitivity = 0.5f
            };
            cloneSource.Validate();
            PosePipelineSettings clone = cloneSource.Clone();
            AssertTrue(Math.Abs(clone.PitchSensitivity - 0.75f) < 0.0001f, "pitch sensitivity should survive cloning");
            AssertTrue(Math.Abs(clone.YawSensitivity + 1.25f) < 0.0001f, "yaw sensitivity should survive cloning");
            AssertTrue(Math.Abs(clone.RollSensitivity - 0.5f) < 0.0001f, "roll sensitivity should survive cloning");
        }

        private static void TestDriftRateIntegration()
        {
            PosePipelineSettings settings = CreateSettings();
            settings.YawDriftRateDegreesPerSecond = 0.01f;
            settings.PitchDriftRateDegreesPerSecond = -0.02f;
            PosePipeline pipeline = new PosePipeline(settings);
            Quaternion output;
            pipeline.TryProcess(Sample(0, Quaternion.Identity), out output);
            pipeline.TryProcess(Sample(Stopwatch.Frequency * 10, Quaternion.Identity), out output);

            Vector3 rotationVector = PoseMath.ToRotationVector(output);
            AssertTrue(Math.Abs(RadiansToDegrees(rotationVector.Y) - 0.1f) < 0.01f, "yaw drift rate should integrate signed degrees per second");
            AssertTrue(Math.Abs(RadiansToDegrees(rotationVector.X) + 0.2f) < 0.01f, "pitch drift rate should integrate signed degrees per second");

            PosePipelineSettings defaults = new PosePipelineSettings();
            AssertTrue(Math.Abs(defaults.YawDriftRateDegreesPerSecond + 0.11f) < 0.0001f, "yaw drift rate should default to the requested -0.11 deg/s counter");
            AssertTrue(defaults.PitchDriftRateDegreesPerSecond == 0.0f, "pitch drift rate should default to zero");
        }

        private static void TestWorldLockedCameraConvention()
        {
            const uint viewportWidth = 1920;
            const uint viewportHeight = 1080;
            Vector3 panelCenter = new Vector3(0.0f, 0.0f, -2.0f);
            Matrix4x4 projection = PanelViewTransform.CreateProjection(viewportWidth, viewportHeight);
            Vector3 neutralCenter = PanelViewTransform.ProjectToNdc(
                panelCenter,
                Matrix4x4.Identity * projection);
            AssertTrue(Math.Abs(neutralCenter.X) < 0.001f && Math.Abs(neutralCenter.Y) < 0.001f, "neutral panel center should project to the viewport center");

            Quaternion yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(30.0f));
            Matrix4x4 worldLockedYaw = PanelViewTransform.CreateWorldViewProjection(yaw, viewportWidth, viewportHeight);
            Matrix4x4 gazeFollowingYaw = Matrix4x4.CreateFromQuaternion(yaw) * projection;
            Vector3 worldLockedYawCenter = PanelViewTransform.ProjectToNdc(panelCenter, worldLockedYaw);
            Vector3 gazeFollowingYawCenter = PanelViewTransform.ProjectToNdc(panelCenter, gazeFollowingYaw);
            AssertTrue(Math.Abs(worldLockedYawCenter.X) > 0.05f, "world-locked yaw should move the finite panel center away from the viewport center");
            AssertTrue(Math.Abs(gazeFollowingYawCenter.X) > 0.05f, "direct camera yaw should produce a distinct gaze-following result");
            AssertTrue(worldLockedYawCenter.X * gazeFollowingYawCenter.X < 0.0f, "inverse world lock and direct gaze following should move in opposite directions");

            Matrix4x4 yawView = PanelViewTransform.CreateWorldLockedView(yaw);
            Vector3 yawUp = Vector3.TransformNormal(Vector3.UnitY, yawView);
            AssertVectorNear(Vector3.UnitY, yawUp, "pure yaw should not rotate the panel up axis in-plane");
            Vector3 yawNormal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, yawView));
            AssertTrue(Math.Abs(Vector3.Dot(Vector3.UnitZ, yawNormal)) < 0.95f, "pure yaw should make the finite panel oblique");

            Quaternion pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, Degrees(25.0f));
            Matrix4x4 pitchView = PanelViewTransform.CreateWorldLockedView(pitch);
            Vector3 pitchCenter = PanelViewTransform.ProjectToNdc(
                panelCenter,
                PanelViewTransform.CreateWorldViewProjection(pitch, viewportWidth, viewportHeight));
            AssertTrue(Math.Abs(pitchCenter.Y) > 0.05f, "world-locked pitch should move the finite panel center vertically");
            Vector3 pitchRight = Vector3.TransformNormal(Vector3.UnitX, pitchView);
            AssertVectorNear(Vector3.UnitX, pitchRight, "pure pitch should not rotate the panel right axis in-plane");
            Vector3 pitchNormal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, pitchView));
            AssertTrue(Math.Abs(Vector3.Dot(Vector3.UnitZ, pitchNormal)) < 0.95f, "pure pitch should make the finite panel oblique");

            Quaternion roll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Degrees(25.0f));
            Quaternion reverseRoll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Degrees(-25.0f));
            Matrix4x4 rollView = PanelViewTransform.CreateWorldLockedView(roll);
            Matrix4x4 reverseRollView = PanelViewTransform.CreateWorldLockedView(reverseRoll);
            Vector3 rollRight = Vector3.TransformNormal(Vector3.UnitX, rollView);
            Vector3 rollUp = Vector3.TransformNormal(Vector3.UnitY, rollView);
            float rollRightY = Vector3.Dot(rollRight, Vector3.UnitY);
            float rollUpX = Vector3.Dot(rollUp, Vector3.UnitX);
            float reverseRollRightY = Vector3.Dot(Vector3.TransformNormal(Vector3.UnitX, reverseRollView), Vector3.UnitY);
            AssertTrue(Math.Abs(rollRightY) > 0.2f && Math.Abs(rollUpX) > 0.2f, "pure roll should rotate the panel in-plane");
            AssertTrue(rollRightY * reverseRollRightY < 0.0f, "opposite roll directions should produce opposite in-plane rotation");
        }

        private static void TestPresentationTransformModes()
        {
            const uint viewportWidth = 1920;
            const uint viewportHeight = 1080;
            Vector3 panelCenter = new Vector3(0.0f, 0.0f, -2.0f);
            Matrix4x4 legacy = PanelViewTransform.CreateWorldViewProjection(Quaternion.Identity, viewportWidth, viewportHeight);
            Matrix4x4 explicitRoomLock = PanelViewTransform.CreateWorldViewProjection(
                Quaternion.Identity,
                Vector3.Zero,
                false,
                viewportWidth,
                viewportHeight);
            Vector3 legacyNdc = PanelViewTransform.ProjectToNdc(panelCenter, legacy);
            Vector3 explicitNdc = PanelViewTransform.ProjectToNdc(panelCenter, explicitRoomLock);
            AssertVectorNear(legacyNdc, explicitNdc, "the explicit room-locked transform should preserve the existing identity behavior");

            Vector3 offset = new Vector3(0.25f, -0.15f, 0.0f);
            Vector3 translatedNdc = PanelViewTransform.ProjectToNdc(
                panelCenter,
                PanelViewTransform.CreateWorldViewProjection(
                    Quaternion.Identity,
                    offset,
                    false,
                    viewportWidth,
                    viewportHeight));
            AssertTrue(translatedNdc.X > legacyNdc.X, "positive panel X offset should move the projected panel right");
            AssertTrue(translatedNdc.Y < legacyNdc.Y, "negative panel Y offset should move the projected panel down");

            Vector3 noTranslation = PanelViewTransform.CreateAngleTranslation(
                Quaternion.CreateFromYawPitchRoll(Degrees(20.0f), Degrees(-10.0f), 0.0f),
                2.0f,
                0.0f);
            AssertTrue(noTranslation.LengthSquared() < 0.000001f, "zero translation sensitivity should preserve the rotational-only output");

            Vector3 oneMeterYawOffset = PanelViewTransform.CreateAngleTranslation(
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(20.0f)),
                1.0f,
                1.0f);
            Vector3 twoMeterYawOffset = PanelViewTransform.CreateAngleTranslation(
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(20.0f)),
                2.0f,
                1.0f);
            AssertTrue(oneMeterYawOffset.X < 0.0f, "positive yaw should create an opposite-direction panel-plane X offset");
            AssertTrue(Math.Abs(twoMeterYawOffset.X - oneMeterYawOffset.X * 2.0f) < 0.0001f, "translation should scale with panel distance");

            Vector3 rollOnlyTranslation = PanelViewTransform.CreateAngleTranslation(
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Degrees(35.0f)),
                2.0f,
                1.0f);
            AssertTrue(rollOnlyTranslation.LengthSquared() < 0.000001f, "roll should not create a translation assist offset");

            Quaternion yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(25.0f));
            Vector3 roomLockedCenter = PanelViewTransform.ProjectToNdc(
                panelCenter,
                PanelViewTransform.CreateWorldViewProjection(yaw, Vector3.Zero, false, viewportWidth, viewportHeight));
            Vector3 headFollowingCenter = PanelViewTransform.ProjectToNdc(
                panelCenter,
                PanelViewTransform.CreateWorldViewProjection(yaw, Vector3.Zero, true, viewportWidth, viewportHeight));
            AssertTrue(roomLockedCenter.X * headFollowingCenter.X < 0.0f, "head-following preview should differ from inverse room lock");
        }

        private static void TestMixedAxisRelativePoseComposition()
        {
            PosePipeline pipeline = CreatePipeline();
            Quaternion neutral = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(Degrees(20.0f), Degrees(-10.0f), Degrees(7.0f)));
            Quaternion relative = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(Degrees(-13.0f), Degrees(18.0f), Degrees(11.0f)));
            Quaternion current = Quaternion.Normalize(Quaternion.Multiply(neutral, relative));
            pipeline.Recenter(Sample(0, neutral));

            Quaternion output;
            pipeline.TryProcess(Sample(Stopwatch.Frequency / 10, current), out output);
            AssertTrue(
                PoseMath.AngularDistanceRadians(relative, output) < Degrees(0.1f),
                "mixed-axis relative pose should use inverse neutral followed by current orientation");
        }

        private static void TestDefaultPoseSettingsAreLowLatency()
        {
            PosePipelineSettings settings = new PosePipelineSettings();
            AssertTrue(settings.SmoothingTimeConstantSeconds == 0.0f, "default pose smoothing should be disabled for low latency");
            AssertTrue(settings.MaxAngularVelocityDegreesPerSecond == 0.0f, "default angular velocity limiting should be disabled for low latency");
            AssertTrue(settings.PoseStabilityLimitDegreesPerSecond == PosePipelineSettings.DefaultPoseStabilityLimitDegreesPerSecond, "default pose stability guard should be enabled");
        }

        private static void TestPoseStabilityGuard()
        {
            PosePipelineSettings settings = CreateSettings();
            settings.PoseStabilityLimitDegreesPerSecond = 900.0f;
            PosePipeline pipeline = new PosePipeline(settings);
            Quaternion output;
            pipeline.TryProcess(Sample(0, Quaternion.Identity), out output);
            pipeline.TryProcess(Sample(Stopwatch.Frequency / 1000, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(180.0f))), out output);
            float outputDegrees = RadiansToDegrees(PoseMath.AngularDistanceRadians(Quaternion.Identity, output));
            AssertTrue(outputDegrees < 1.0f, "the default pose stability guard should bound an impossible one-millisecond pose jump");
        }

        private static void TestPanelCurvatureDirection()
        {
            PanelSettings settings = new PanelSettings
            {
                PanelWidthMeters = 1.6f,
                PanelHeightMeters = 0.9f,
                PanelDistanceMeters = 2.0f,
                CurvatureRadiusXMeters = 4.0f,
                CurvatureRadiusYMeters = 4.0f
            };
            Vector3 center = PanelGeometry.CreatePosition(settings, 0.5f, 0.5f);
            Vector3 left = PanelGeometry.CreatePosition(settings, 0.0f, 0.5f);
            Vector3 right = PanelGeometry.CreatePosition(settings, 1.0f, 0.5f);
            Vector3 top = PanelGeometry.CreatePosition(settings, 0.5f, 0.0f);
            Vector3 bottom = PanelGeometry.CreatePosition(settings, 0.5f, 1.0f);
            Vector3 corner = PanelGeometry.CreatePosition(settings, 0.0f, 0.0f);
            AssertTrue(left.Z > center.Z && right.Z > center.Z, "curved monitor edges should be closer to the viewer than the center");
            AssertTrue(top.Z > center.Z && bottom.Z > center.Z, "spherical monitor top and bottom edges should be closer to the viewer than the center");
            AssertTrue(corner.Z > left.Z && corner.Z > top.Z, "spherical monitor corners should be closest to the viewer");
            AssertTrue(Math.Abs(left.Z - right.Z) < 0.0001f, "curved monitor depth should be symmetric");
            AssertTrue(Math.Abs(top.Z - bottom.Z) < 0.0001f, "spherical monitor vertical depth should be symmetric");
            AssertTrue(Math.Abs(left.X + right.X) < 0.0001f, "curved monitor width should be symmetric");
            AssertTrue(Math.Abs(top.Y + bottom.Y) < 0.0001f, "spherical monitor height should be symmetric");
        }

        private static void TestIndependentPanelCurvature()
        {
            PanelSettings horizontalOnly = new PanelSettings
            {
                PanelWidthMeters = 1.6f,
                PanelHeightMeters = 0.9f,
                PanelDistanceMeters = 2.0f,
                CurvatureRadiusXMeters = 4.0f,
                CurvatureRadiusYMeters = 0.0f
            };
            Vector3 horizontalCenter = PanelGeometry.CreatePosition(horizontalOnly, 0.5f, 0.5f);
            Vector3 horizontalEdge = PanelGeometry.CreatePosition(horizontalOnly, 0.0f, 0.5f);
            Vector3 horizontalTop = PanelGeometry.CreatePosition(horizontalOnly, 0.5f, 0.0f);
            AssertTrue(horizontalEdge.Z > horizontalCenter.Z, "X curvature should bend horizontal edges toward the viewer");
            AssertTrue(Math.Abs(horizontalTop.Z - horizontalCenter.Z) < 0.0001f, "X curvature should leave the vertical axis flat");

            PanelSettings verticalOnly = horizontalOnly.Clone();
            verticalOnly.CurvatureRadiusXMeters = 0.0f;
            verticalOnly.CurvatureRadiusYMeters = 4.0f;
            Vector3 verticalCenter = PanelGeometry.CreatePosition(verticalOnly, 0.5f, 0.5f);
            Vector3 verticalEdge = PanelGeometry.CreatePosition(verticalOnly, 0.5f, 0.0f);
            Vector3 verticalSide = PanelGeometry.CreatePosition(verticalOnly, 0.0f, 0.5f);
            AssertTrue(verticalEdge.Z > verticalCenter.Z, "Y curvature should bend vertical edges toward the viewer");
            AssertTrue(Math.Abs(verticalSide.Z - verticalCenter.Z) < 0.0001f, "Y curvature should leave the horizontal axis flat");
        }

        private static void TestDistanceProfiles()
        {
            ViewerSettings settings = new ViewerSettings();
            AssertTrue(settings.DistanceProfiles[0].Key == "Near", "first distance profile should be Near");
            AssertTrue(settings.DistanceProfiles[1].Key == "Mid", "second distance profile should be Mid");
            AssertTrue(settings.DistanceProfiles[2].Key == "Far", "third distance profile should be Far");
            AssertTrue(settings.DistanceProfiles[3].Key == "Furthest", "fourth distance profile should be Furthest");
            AssertTrue(Math.Abs(settings.DistanceProfiles[1].PanelDistanceMeters - 0.85f) < 0.0001f, "mid profile should sit between near and far");
            settings.ActiveDistanceProfile = "Near";
            settings.DistanceProfiles[0].PanelDistanceMeters = 0.8f;
            settings.DistanceProfiles[0].YawSensitivity = -1.2f;
            settings.DistanceProfiles[1].PanelDistanceMeters = 1.4f;
            settings.DistanceProfiles[2].PanelDistanceMeters = 2.8f;
            settings.DistanceProfiles[3].PanelDistanceMeters = 3.6f;
            settings.Validate();
            ViewerSettings clone = settings.Clone();
            clone.DistanceProfiles[0].PanelDistanceMeters = 0.9f;
            AssertTrue(Math.Abs(settings.DistanceProfiles[0].PanelDistanceMeters - 0.8f) < 0.0001f, "profile cloning should be deep");
            AssertTrue(clone.ActiveDistanceProfile == "Near", "active profile should survive cloning");
            AssertTrue(Math.Abs(clone.DistanceProfiles[0].YawSensitivity + 1.2f) < 0.0001f, "profile sensitivity should survive cloning");
        }

        private static void TestFirstSampleRecenters()
        {
            PosePipeline pipeline = CreatePipeline();
            Quaternion output;
            pipeline.TryProcess(Sample(0, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(70))), out output);
            AssertAngleNear(0.0f, output, "first sample should be neutral");
        }

        private static void TestAutoRecenterWaitsForStartup()
        {
            PosePipelineSettings settings = CreateSettings();
            settings.AutoRecenterDelaySeconds = 1.0f;
            PosePipeline pipeline = new PosePipeline(settings);
            Quaternion output;
            Quaternion settlingPose = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(35.0f));
            pipeline.TryProcess(Sample(0, Quaternion.Identity), out output);
            AssertFalse(pipeline.HasNeutral, "auto recenter should wait for the startup delay");
            pipeline.TryProcess(Sample(Stopwatch.Frequency / 2, settlingPose), out output);
            AssertFalse(pipeline.HasNeutral, "auto recenter should remain pending before the delay expires");
            pipeline.TryProcess(Sample(Stopwatch.Frequency, settlingPose), out output);
            AssertFalse(pipeline.HasNeutral, "a moving startup pose should restart the stability window");
            pipeline.TryProcess(Sample(Stopwatch.Frequency * 3 / 2, settlingPose), out output);
            AssertTrue(pipeline.HasNeutral, "auto recenter should complete after a stable startup window");
            AssertAngleNear(0.0f, output, "the latest startup pose should become neutral");
        }

        private static void TestRecenterProducesIdentity()
        {
            PosePipeline pipeline = CreatePipeline();
            PoseSample sample = Sample(0, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(70)));
            pipeline.Recenter(sample);
            Quaternion output;
            pipeline.TryProcess(sample, out output);
            AssertAngleNear(0.0f, output, "recenter should produce identity");
        }

        private static void TestSmoothingUsesTimeConstant()
        {
            PosePipelineSettings settings = CreateSettings();
            settings.SmoothingTimeConstantSeconds = 0.1f;
            PosePipeline pipeline = new PosePipeline(settings);
            Quaternion output;
            pipeline.TryProcess(Sample(0, Quaternion.Identity), out output);
            pipeline.TryProcess(Sample(Stopwatch.Frequency / 10, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(90))), out output);
            float outputDegrees = RadiansToDegrees(PoseMath.AngularDistanceRadians(Quaternion.Identity, output));
            AssertTrue(outputDegrees > 45.0f && outputDegrees < 70.0f, "smoothing should follow elapsed time");
        }

        private static void TestMaximumAngularVelocity()
        {
            PosePipelineSettings settings = CreateSettings();
            settings.MaxAngularVelocityDegreesPerSecond = 90.0f;
            PosePipeline pipeline = new PosePipeline(settings);
            Quaternion output;
            pipeline.TryProcess(Sample(0, Quaternion.Identity), out output);
            pipeline.TryProcess(Sample(Stopwatch.Frequency / 100, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(180))), out output);
            float outputDegrees = RadiansToDegrees(PoseMath.AngularDistanceRadians(Quaternion.Identity, output));
            AssertTrue(outputDegrees <= 9.1f, "maximum angular velocity should cap the step");
        }

        private static void TestRollLockRemovesZTwist()
        {
            PosePipelineSettings settings = CreateSettings();
            settings.RollLock = true;
            PosePipeline pipeline = new PosePipeline(settings);
            Quaternion output;
            pipeline.TryProcess(Sample(0, Quaternion.Identity), out output);
            pipeline.TryProcess(Sample(Stopwatch.Frequency / 100, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Degrees(90))), out output);
            AssertAngleNear(0.0f, output, "roll lock should remove Z-axis twist");
        }

        private static void TestHorizonLockRemovesRoll()
        {
            PosePipelineSettings settings = CreateSettings();
            settings.HorizonLock = true;
            PosePipeline pipeline = new PosePipeline(settings);
            Quaternion yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(35.0f));
            Quaternion pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, Degrees(-20.0f));
            Quaternion noRoll = PoseMath.RemoveRollAroundForward(Quaternion.Multiply(yaw, pitch), Vector3.UnitY);
            Quaternion localRoll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Degrees(45.0f));
            Quaternion withRoll = Quaternion.Multiply(noRoll, localRoll);
            Quaternion output;
            pipeline.TryProcess(Sample(0, Quaternion.Identity), out output);
            pipeline.TryProcess(Sample(Stopwatch.Frequency / 100, withRoll), out output);
            AssertTrue(PoseMath.AngularDistanceRadians(output, noRoll) < Degrees(0.2f), "horizon lock should remove camera roll while preserving yaw and pitch");
        }

        private static void TestLatestPoseStore()
        {
            LatestPoseStore store = new LatestPoseStore();
            PoseSample expected = Sample(42, Quaternion.CreateFromAxisAngle(Vector3.UnitX, Degrees(10)));
            PoseSample actual;
            AssertFalse(store.TryRead(out actual), "empty store should have no sample");
            store.Publish(expected);
            AssertTrue(store.TryRead(out actual), "published sample should be readable");
            AssertTrue(actual.TimestampTicks == expected.TimestampTicks, "latest sample timestamp should be preserved");
            AssertAngleNear(10.0f, actual.Orientation, "latest sample orientation should be preserved");
            store.Clear();
            AssertFalse(store.TryRead(out actual), "clearing the pose store should remove stale samples");
        }

        private static void TestLatestPoseObservationStore()
        {
            PoseSample sample = Sample(42, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(10.0f)));
            PoseObservation expected = new PoseObservation(sample, new Vector4(1.0f, 2.0f, 3.0f, 4.0f), true);
            LatestPoseObservationStore store = new LatestPoseObservationStore();
            PoseObservation actual;
            AssertFalse(store.TryRead(out actual), "empty observation store should have no observation");
            store.Publish(expected);
            AssertTrue(store.TryRead(out actual), "published observation should be readable");
            AssertTrue(actual.TimestampTicks == expected.TimestampTicks, "observation timestamp should be preserved");
            AssertTrue(actual.HasNativeComponents, "native component availability should be preserved");
            AssertTrue(actual.NativeComponents == expected.NativeComponents, "native components should be preserved");
            store.Clear();
            AssertFalse(store.TryRead(out actual), "clearing the observation store should remove the observation");
        }

        private static void TestPosePresentationSnapshot()
        {
            Quaternion orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(12.0f));
            PosePresentationSnapshot snapshot = new PosePresentationSnapshot(
                100,
                90,
                orientation,
                80,
                1920,
                1200,
                7,
                "\\\\.\\DISPLAY1",
                "\\\\.\\DISPLAY2",
                "world-locked");
            AssertTrue(snapshot.PresentedTimestampTicks == 100, "presentation timestamp should be preserved");
            AssertTrue(snapshot.PoseSampleTimestampTicks == 90, "presented pose timestamp should be preserved");
            AssertTrue(snapshot.PresentationCount == 7, "presentation count should be preserved");
            AssertTrue(snapshot.CameraMode == "world-locked", "camera mode should be preserved");
            AssertAngleNear(12.0f, snapshot.ProcessedOrientation, "presented orientation should be preserved");
        }

        private static void TestPoseEvidenceTargetsAndSerialization()
        {
            IList<PoseEvidenceTarget> targets = PoseEvidenceTargets.CreateDefault();
            AssertTrue(targets.Count == 7, "pose evidence should provide seven fixed targets");
            AssertTrue(targets[0].Label == "neutral" && targets[0].TargetAngleDegrees == 0.0f, "neutral target should be labeled zero degrees");

            string directory = Path.Combine(Path.GetTempPath(), "PhoenixAirViewerEvidenceTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                PoseEvidenceManifest manifest = new PoseEvidenceManifest
                {
                    SchemaVersion = 1,
                    SessionId = "test-session",
                    CreatedUtc = DateTime.UtcNow,
                    ProcessId = 42,
                    Runtime = "test",
                    ProcessArchitecture = "x64",
                    StopwatchFrequency = 1000,
                    NativeQuaternionLayout = "Wxyz",
                    SensorToRenderer = new PoseEvidenceQuaternion(Quaternion.Identity),
                    CameraMode = "world-locked",
                    CaptureDelayMilliseconds = 3000,
                    Panel = new PanelSettings(),
                    Displays = new List<PoseEvidenceDisplay>()
                };
                PoseEvidenceRecord record = new PoseEvidenceRecord
                {
                    SchemaVersion = 1,
                    EvidenceId = "0001-neutral",
                    Sequence = 1,
                    Label = "neutral",
                    TargetAxis = "none",
                    TargetAngleDegrees = 0.0f,
                    PressedUtc = DateTime.UtcNow,
                    PressedMonotonicTicks = 100,
                    CaptureStatus = "complete",
                    Screenshots = new List<PoseEvidenceScreenshot>()
                };
                using (PoseEvidenceSessionWriter writer = new PoseEvidenceSessionWriter(directory, manifest))
                {
                    writer.Write(record);
                }

                AssertTrue(File.Exists(Path.Combine(directory, "manifest.json")), "evidence manifest should be written");
                string evidenceJson = File.ReadAllText(Path.Combine(directory, "evidence.jsonl"));
                AssertTrue(evidenceJson.Contains("0001-neutral"), "evidence JSONL should contain the evidence ID");
                AssertTrue(evidenceJson.Contains("complete"), "evidence JSONL should contain the capture status");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void TestPoseCalibration()
        {
            List<PoseSample> neutral = new List<PoseSample> { Sample(0, Quaternion.Identity), Sample(1, Quaternion.Identity) };
            List<PoseSample> yawRight = AxisSamples(Vector3.UnitY, 30.0f);
            List<PoseSample> yawLeft = AxisSamples(Vector3.UnitY, -30.0f);
            List<PoseSample> pitchUp = AxisSamples(Vector3.UnitX, 25.0f);
            List<PoseSample> pitchDown = AxisSamples(Vector3.UnitX, -25.0f);
            List<PoseSample> rollRight = AxisSamples(Vector3.UnitZ, 20.0f);
            List<PoseSample> rollLeft = AxisSamples(Vector3.UnitZ, -20.0f);
            PoseCalibrationResult result;
            string error;
            AssertTrue(
                PoseCalibration.TryCompute(neutral, yawRight, yawLeft, pitchUp, pitchDown, rollRight, rollLeft, out result, out error),
                "identity calibration should succeed: " + error);
            AssertTrue(result.AxisErrorDegrees < 0.1f, "identity calibration should have no axis error");
            AssertTrue(PoseMath.AngularDistanceRadians(result.SensorToRenderer, Quaternion.Identity) < 0.001f, "identity calibration should produce identity basis");
        }

        private static void TestPoseCalibrationRotatedBasis()
        {
            Quaternion expectedBasis = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(Degrees(24.0f), Degrees(-13.0f), Degrees(31.0f)));
            Quaternion inverseBasis = Quaternion.Inverse(expectedBasis);
            List<PoseSample> neutral = new List<PoseSample> { Sample(0, Quaternion.Identity) };
            List<PoseSample> yawRight = AxisSamples(Vector3.Transform(Vector3.UnitY, inverseBasis), 30.0f);
            List<PoseSample> yawLeft = AxisSamples(Vector3.Transform(Vector3.UnitY, inverseBasis), -30.0f);
            List<PoseSample> pitchUp = AxisSamples(Vector3.Transform(Vector3.UnitX, inverseBasis), 25.0f);
            List<PoseSample> pitchDown = AxisSamples(Vector3.Transform(Vector3.UnitX, inverseBasis), -25.0f);
            List<PoseSample> rollRight = AxisSamples(Vector3.Transform(Vector3.UnitZ, inverseBasis), 20.0f);
            List<PoseSample> rollLeft = AxisSamples(Vector3.Transform(Vector3.UnitZ, inverseBasis), -20.0f);
            PoseCalibrationResult result;
            string error;
            AssertTrue(
                PoseCalibration.TryCompute(neutral, yawRight, yawLeft, pitchUp, pitchDown, rollRight, rollLeft, out result, out error),
                "rotated calibration should succeed: " + error);
            AssertTrue(
                PoseMath.AngularDistanceRadians(result.SensorToRenderer, expectedBasis) < Degrees(1.0f),
                "rotated calibration should recover the sensor basis");
        }

        private static List<PoseSample> AxisSamples(Vector3 axis, float degrees)
        {
            Quaternion orientation = Quaternion.CreateFromAxisAngle(axis, Degrees(degrees));
            return new List<PoseSample> { Sample(0, orientation), Sample(1, orientation) };
        }

        private static void TestRenderSchedulerRetainsStaticFrame()
        {
            DesktopViewerFrameScheduler scheduler = new DesktopViewerFrameScheduler(60.0);
            long firstRenderTicks = 1;
            AssertFalse(scheduler.TrySchedule(DesktopCaptureStatus.Timeout, firstRenderTicks), "a timeout before the first frame should not render");
            AssertTrue(scheduler.TrySchedule(DesktopCaptureStatus.FrameReady, firstRenderTicks), "the first desktop frame should render");
            AssertTrue(scheduler.HasLatestFrame, "the scheduler should retain the latest desktop frame");

            long halfIntervalTicks = firstRenderTicks + scheduler.MinimumFrameIntervalTicks / 2;
            AssertFalse(scheduler.TrySchedule(DesktopCaptureStatus.Timeout, halfIntervalTicks), "pose-only rendering should be cadence limited");
            AssertTrue(scheduler.GetWaitMilliseconds(halfIntervalTicks) > 0, "cadence limiting should provide a wait interval");

            long nextRenderTicks = firstRenderTicks + scheduler.MinimumFrameIntervalTicks;
            AssertTrue(scheduler.TryScheduleLatestFrame(nextRenderTicks), "a retained frame should render without a new capture result");

            PosePipeline pipeline = CreatePipeline();
            Quaternion orientation;
            pipeline.TryProcess(Sample(firstRenderTicks, Quaternion.Identity), out orientation);
            pipeline.TryProcess(Sample(nextRenderTicks, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(30.0f))), out orientation);
            AssertTrue(RadiansToDegrees(PoseMath.AngularDistanceRadians(Quaternion.Identity, orientation)) > 29.0f, "the pose-only render should use the newest pose");
        }

        private static void TestPosePollingWorkerPublishesLatest()
        {
            PoseSample expected = Sample(PoseClock.NowTicks(), Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(12.0f)));
            FakePoseSource source = new FakePoseSource(expected);
            LatestPoseStore store = new LatestPoseStore();
            using (PosePollingWorker worker = new PosePollingWorker(source, store, NullViewerLogger.Instance))
            {
                worker.Start();
                long deadline = PoseClock.NowTicks() + Stopwatch.Frequency;
                PoseSample actual;
                while (!store.TryRead(out actual) && PoseClock.NowTicks() < deadline)
                {
                    Thread.Sleep(5);
                }

                AssertTrue(store.TryRead(out actual), "the pose worker should publish a sample");
                AssertTrue(actual.TimestampTicks == expected.TimestampTicks, "the pose worker should preserve the sample timestamp");
                AssertTrue(worker.IsConnected, "the pose worker should report a connected source");
            }

            AssertTrue(source.DisconnectCount == 1, "the pose worker should disconnect its source once during disposal");
        }

        private static void TestPosePollingWorkerReconnectsAfterFailures()
        {
            PoseSample expected = Sample(PoseClock.NowTicks(), Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(18.0f)));
            FakePoseSource source = new FakePoseSource(expected, 3);
            LatestPoseStore store = new LatestPoseStore();
            using (PosePollingWorker worker = new PosePollingWorker(source, store, NullViewerLogger.Instance))
            {
                worker.Start();
                long deadline = PoseClock.NowTicks() + Stopwatch.Frequency * 6;
                PoseSample actual;
                while (!store.TryRead(out actual) && PoseClock.NowTicks() < deadline)
                {
                    Thread.Sleep(25);
                }

                AssertTrue(store.TryRead(out actual), "the pose worker should recover and publish after failed reads");
                AssertTrue(source.ConnectCount >= 2, "failed reads should trigger a reconnect attempt");
                AssertTrue(source.DisconnectCount >= 1, "failed reads should disconnect the broken source");
            }
        }

        private static void TestPosePollingWorkerCanStopBeforeStart()
        {
            FakePoseSource source = new FakePoseSource(Sample(PoseClock.NowTicks(), Quaternion.Identity));
            using (PosePollingWorker worker = new PosePollingWorker(source, new LatestPoseStore(), NullViewerLogger.Instance))
            {
                AssertTrue(worker.Stop(100), "an unstarted pose worker should stop cleanly");
            }
        }

        private static void TestPanelSettingsValidation()
        {
            PanelSettings settings = new PanelSettings
            {
                PanelWidthMeters = 2.0f,
                PanelHeightMeters = 1.125f,
                PanelDistanceMeters = 2.5f,
                CurvatureRadiusMeters = 1.5f
            };
            settings.Validate();
            PanelSettings clone = settings.Clone();
            AssertTrue(Math.Abs(clone.PanelWidthMeters - settings.PanelWidthMeters) < 0.0001f, "panel width should survive cloning");
            AssertTrue(Math.Abs(clone.CurvatureRadiusMeters - settings.CurvatureRadiusMeters) < 0.0001f, "panel curvature should survive cloning");

            PanelSettings gentleCurve = new PanelSettings
            {
                CurvatureRadiusMeters = PanelSettings.GentleCurveRadiusMeters
            };
            gentleCurve.Validate();
            AssertTrue(gentleCurve.CurvatureRadiusMeters > 0.0f, "the gentle curve preset should create a cylindrical panel");
        }

        private static void TestWideCurvedMonitorDefaults()
        {
            ViewerSettings settings = new ViewerSettings();
            AssertTrue(settings.ActiveDistanceProfile == "Far", "fresh settings should use the Far distance profile for the wide monitor default");
            AssertTrue(Math.Abs(settings.Panel.PanelWidthMeters - PanelSettings.WideCurvePanelWidthMeters) < 0.0001f, "fresh settings should use the wide monitor width");
            AssertTrue(Math.Abs(settings.Panel.PanelHeightMeters - PanelSettings.WideCurvePanelHeightMeters) < 0.0001f, "fresh settings should use the wide monitor height");
            AssertTrue(Math.Abs(settings.Panel.CurvatureRadiusMeters - PanelSettings.WideCurveRadiusMeters) < 0.0001f, "fresh settings should use the wide monitor curvature");
            AssertTrue(settings.Panel.TranslationSensitivity == 0.0f, "wide monitor defaults should leave translation disabled");
        }

        private static void TestViewerSettingsPersistence()
        {
            string directory = Path.Combine(Path.GetTempPath(), "PhoenixAirViewerTests-" + Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "settings.json");
            try
            {
                ViewerSettings settings = new ViewerSettings
                {
                    SourceDisplayName = "\\\\.\\DISPLAY1",
                    OutputDisplayName = "\\\\.\\DISPLAY2",
                    RecenterHotkey = "Ctrl+Alt+F12",
                    FileLoggingEnabled = false
                };
                settings.Pose.RollLock = true;
                settings.Pose.SensorToRenderer = Quaternion.CreateFromAxisAngle(Vector3.UnitX, Degrees(15.0f));
                settings.Pose.PitchSensitivity = 0.75f;
                settings.Pose.YawSensitivity = -1.25f;
                settings.Pose.RollSensitivity = 0.5f;
                settings.Pose.PitchDriftRateDegreesPerSecond = -0.12f;
                settings.Pose.YawDriftRateDegreesPerSecond = 0.34f;
                settings.ActiveDistanceProfile = "Mid";
                settings.DistanceProfiles[0].PanelDistanceMeters = 0.8f;
                settings.DistanceProfiles[1].PanelDistanceMeters = 1.5f;
                settings.DistanceProfiles[1].PanelDistanceMeters = 3.0f;
                settings.DistanceProfiles[1].TranslationSensitivity = 0.6f;
                settings.Panel.PanelWidthMeters = 2.0f;
                settings.Panel.CurvatureRadiusMeters = 2.5f;

                ViewerSettingsStore store = new ViewerSettingsStore(filePath);
                store.Save(settings);
                settings.Panel.PanelWidthMeters = 2.5f;
                store.Save(settings);
                File.WriteAllText(filePath, "{ invalid json", System.Text.Encoding.UTF8);
                ViewerSettings loaded = store.Load();
                AssertTrue(loaded.SourceDisplayName == settings.SourceDisplayName, "source display should survive persistence: " + (store.LastLoadError ?? "no load error"));
                AssertTrue(loaded.OutputDisplayName == settings.OutputDisplayName, "output display should survive persistence");
                AssertTrue(loaded.Pose.RollLock, "pose settings should survive persistence");
                AssertTrue(PoseMath.AngularDistanceRadians(loaded.Pose.SensorToRenderer, settings.Pose.SensorToRenderer) < 0.001f, "quaternion mapping should survive persistence");
                AssertTrue(Math.Abs(loaded.Pose.PitchSensitivity - settings.Pose.PitchSensitivity) < 0.0001f, "pitch sensitivity should survive persistence");
                AssertTrue(Math.Abs(loaded.Pose.YawSensitivity - settings.Pose.YawSensitivity) < 0.0001f, "yaw sensitivity should survive persistence");
                AssertTrue(Math.Abs(loaded.Pose.RollSensitivity - settings.Pose.RollSensitivity) < 0.0001f, "roll sensitivity should survive persistence");
                AssertTrue(Math.Abs(loaded.Pose.PitchDriftRateDegreesPerSecond - settings.Pose.PitchDriftRateDegreesPerSecond) < 0.0001f, "pitch drift rate should survive persistence");
                AssertTrue(Math.Abs(loaded.Pose.YawDriftRateDegreesPerSecond - settings.Pose.YawDriftRateDegreesPerSecond) < 0.0001f, "yaw drift rate should survive persistence");
                AssertTrue(loaded.ActiveDistanceProfile == settings.ActiveDistanceProfile, "active distance profile should survive persistence");
                AssertTrue(Math.Abs(loaded.DistanceProfiles[1].PanelDistanceMeters - settings.DistanceProfiles[1].PanelDistanceMeters) < 0.0001f, "mid profile distance should survive persistence");
                AssertTrue(Math.Abs(loaded.DistanceProfiles[1].TranslationSensitivity - settings.DistanceProfiles[1].TranslationSensitivity) < 0.0001f, "profile translation should survive persistence");
                AssertTrue(Math.Abs(loaded.Panel.PanelWidthMeters - 2.0f) < 0.0001f, "the backup should restore the last valid settings snapshot");
                AssertTrue(Math.Abs(loaded.Panel.CurvatureRadiusMeters - settings.Panel.CurvatureRadiusMeters) < 0.0001f, "panel settings should survive persistence");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void TestViewerSettingsMigratesDefaultAirMapping()
        {
            string directory = Path.Combine(Path.GetTempPath(), "PhoenixAirViewerMigrationTests-" + Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "settings.json");
            string legacyV1Path = Path.Combine(directory, "settings-v1.json");
            string currentLegacyPath = Path.Combine(directory, "settings-current-legacy.json");
            string customPath = Path.Combine(directory, "settings-custom.json");
            string farProfilePath = Path.Combine(directory, "settings-far-profile.json");
            try
            {
                ViewerSettings legacySettings = new ViewerSettings();
                legacySettings.Pose.SensorToRenderer = PosePipelineSettings.LegacyDefaultAirSensorToRenderer;
                legacySettings.Pose.SmoothingTimeConstantSeconds = 0.035f;
                legacySettings.Pose.MaxAngularVelocityDegreesPerSecond = 720.0f;
                ViewerSettingsStore store = new ViewerSettingsStore(filePath);
                store.Save(legacySettings);
                string json = File.ReadAllText(filePath);
                json = json.Replace("\"SchemaVersion\": " + ViewerSettings.CurrentSchemaVersion, "\"SchemaVersion\": 2");
                File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);

                ViewerSettings migrated = store.Load();
                AssertTrue(migrated.SchemaVersion == ViewerSettings.CurrentSchemaVersion, "legacy settings should be upgraded to the current schema");
                AssertTrue(
                    PoseMath.AngularDistanceRadians(migrated.Pose.SensorToRenderer, PosePipelineSettings.DefaultAirSensorToRenderer) < 0.001f,
                    "legacy Air mapping should migrate to the current default mapping");
                AssertTrue(migrated.Pose.SmoothingTimeConstantSeconds == 0.0f, "legacy smoothing default should migrate to the low-latency default");
                AssertTrue(migrated.Pose.MaxAngularVelocityDegreesPerSecond == 0.0f, "legacy angular velocity limit should migrate to the low-latency default");
                AssertTrue(migrated.Pose.PitchSensitivity == PosePipelineSettings.DefaultPitchSensitivity, "legacy pitch sensitivity should migrate to the current default");
                AssertTrue(migrated.Pose.YawSensitivity == PosePipelineSettings.DefaultYawSensitivity, "legacy yaw sensitivity should migrate to the current default");
                AssertTrue(migrated.Pose.RollSensitivity == PosePipelineSettings.DefaultRollSensitivity, "legacy roll sensitivity should migrate to the current default");
                AssertTrue(migrated.DistanceProfiles.Count == 4, "legacy settings should migrate to four distance profiles");
                AssertTrue(migrated.ActiveDistanceProfile == "Far", "legacy default distance should select the Far profile");

                ViewerSettings currentLegacySettings = new ViewerSettings();
                currentLegacySettings.Pose.SensorToRenderer = PosePipelineSettings.LegacyDefaultAirSensorToRenderer;
                ViewerSettingsStore currentLegacyStore = new ViewerSettingsStore(currentLegacyPath);
                currentLegacyStore.Save(currentLegacySettings);
                ViewerSettings migratedCurrentLegacy = currentLegacyStore.Load();
                AssertTrue(
                    PoseMath.AngularDistanceRadians(migratedCurrentLegacy.Pose.SensorToRenderer, PosePipelineSettings.DefaultAirSensorToRenderer) < 0.001f,
                    "the shipped legacy Air mapping should migrate even when stored at the current schema");

                ViewerSettings identitySettings = new ViewerSettings();
                identitySettings.Pose.SensorToRenderer = Quaternion.Identity;
                ViewerSettingsStore identityStore = new ViewerSettingsStore(legacyV1Path);
                identityStore.Save(identitySettings);
                string identityJson = File.ReadAllText(legacyV1Path);
                identityJson = identityJson.Replace("\"SchemaVersion\": " + ViewerSettings.CurrentSchemaVersion, "\"SchemaVersion\": 1");
                File.WriteAllText(legacyV1Path, identityJson, System.Text.Encoding.UTF8);

                ViewerSettings migratedIdentity = identityStore.Load();
                AssertTrue(
                    PoseMath.AngularDistanceRadians(migratedIdentity.Pose.SensorToRenderer, PosePipelineSettings.DefaultAirSensorToRenderer) < 0.001f,
                    "legacy identity mapping should migrate to the current Air default mapping");

                ViewerSettings customSettings = new ViewerSettings();
                customSettings.Pose.SensorToRenderer = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(12.0f));
                customSettings.Pose.SmoothingTimeConstantSeconds = 0.02f;
                customSettings.Pose.MaxAngularVelocityDegreesPerSecond = 900.0f;
                ViewerSettingsStore customStore = new ViewerSettingsStore(customPath);
                customStore.Save(customSettings);
                string customJson = File.ReadAllText(customPath);
                customJson = customJson.Replace("\"SchemaVersion\": " + ViewerSettings.CurrentSchemaVersion, "\"SchemaVersion\": 2");
                File.WriteAllText(customPath, customJson, System.Text.Encoding.UTF8);

                ViewerSettings migratedCustom = customStore.Load();
                AssertTrue(
                    PoseMath.AngularDistanceRadians(migratedCustom.Pose.SensorToRenderer, customSettings.Pose.SensorToRenderer) < 0.001f,
                    "custom mapping should survive settings migration");
                AssertTrue(Math.Abs(migratedCustom.Pose.SmoothingTimeConstantSeconds - 0.02f) < 0.0001f, "custom smoothing should survive settings migration");
                AssertTrue(Math.Abs(migratedCustom.Pose.MaxAngularVelocityDegreesPerSecond - 900.0f) < 0.0001f, "custom angular velocity limit should survive settings migration");

                ViewerSettings farProfileSettings = new ViewerSettings();
                farProfileSettings.ActiveDistanceProfile = "Mid";
                farProfileSettings.DistanceProfiles[1].YawSensitivity = -1.65f;
                ViewerSettingsStore farProfileStore = new ViewerSettingsStore(farProfilePath);
                farProfileStore.Save(farProfileSettings);
                string farProfileJson = File.ReadAllText(farProfilePath);
                farProfileJson = farProfileJson.Replace("\"SchemaVersion\": " + ViewerSettings.CurrentSchemaVersion, "\"SchemaVersion\": 5");
                farProfileJson = farProfileJson.Replace("\"Mid\"", "\"Far\"");
                File.WriteAllText(farProfilePath, farProfileJson, System.Text.Encoding.UTF8);

                ViewerSettings migratedFarProfile = farProfileStore.Load();
                AssertTrue(migratedFarProfile.ActiveDistanceProfile == "Far", "legacy Far profile should migrate to the active Far profile");
                AssertTrue(Math.Abs(migratedFarProfile.DistanceProfiles[2].YawSensitivity + 1.65f) < 0.0001f, "Far profile tuning should survive migration into Far");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void TestHotkeySettings()
        {
            uint modifiers;
            uint virtualKey;
            string error;
            AssertTrue(HotkeySettings.TryParse("Ctrl+Alt+Space", out modifiers, out virtualKey, out error), "default hotkey should parse: " + error);
            AssertTrue(modifiers == (HotkeySettings.ModControl | HotkeySettings.ModAlt), "default hotkey modifiers should parse");
            AssertTrue(virtualKey == 0x20, "default hotkey key should parse");
            AssertTrue(HotkeySettings.TryParse("Shift+F12", out modifiers, out virtualKey, out error), "function-key hotkey should parse: " + error);
            AssertTrue(virtualKey == 0x7B, "F12 should map to the Windows virtual-key value");
            AssertFalse(HotkeySettings.TryParse("Space", out modifiers, out virtualKey, out error), "a hotkey without a modifier should be rejected");
        }

        private static void TestViewerLogging()
        {
            string directory = Path.Combine(Path.GetTempPath(), "PhoenixAirViewerLogs-" + Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "viewer.jsonl");
            try
            {
                using (FileViewerLogger logger = new FileViewerLogger(filePath))
                {
                    logger.Information("test.started", "diagnostic record");
                    logger.Error("test.failed", "failure record", new InvalidOperationException("synthetic failure"));
                }

                string contents = File.ReadAllText(filePath);
                AssertTrue(contents.Contains("test.started"), "file logger should write information events");
                AssertTrue(contents.Contains("synthetic failure"), "file logger should write exception details");
                AssertFalse(NullViewerLogger.Instance.IsEnabled, "null logger should be disabled");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static PosePipeline CreatePipeline()
        {
            return new PosePipeline(CreateSettings());
        }

        private static PosePipelineSettings CreateSettings()
        {
            return new PosePipelineSettings
            {
                SmoothingTimeConstantSeconds = 0.0f,
                MaxAngularVelocityDegreesPerSecond = 0.0f,
                PoseStabilityLimitDegreesPerSecond = 0.0f,
                PitchSensitivity = 1.0f,
                YawSensitivity = 1.0f,
                RollSensitivity = 1.0f,
                PitchDriftRateDegreesPerSecond = 0.0f,
                YawDriftRateDegreesPerSecond = 0.0f,
                SensorToRenderer = Quaternion.Identity,
                AutoRecenterDelaySeconds = 0.0f,
                AutoRecenterOnFirstSample = true
            };
        }

        private static PoseSample Sample(long timestampTicks, Quaternion orientation)
        {
            return new PoseSample(timestampTicks, orientation);
        }

        private static float Degrees(float degrees)
        {
            return degrees * (float)Math.PI / 180.0f;
        }

        private static float RadiansToDegrees(float radians)
        {
            return radians * 180.0f / (float)Math.PI;
        }

        private static void AssertAngleNear(float expectedDegrees, Quaternion actual, string message)
        {
            float actualDegrees = RadiansToDegrees(PoseMath.AngularDistanceRadians(Quaternion.Identity, actual));
            AssertTrue(Math.Abs(expectedDegrees - actualDegrees) < 0.1f, message + ". Expected " + expectedDegrees + ", got " + actualDegrees + ".");
        }

        private static void AssertVectorNear(Vector3 expected, Vector3 actual, string message)
        {
            AssertTrue(Vector3.Distance(expected, actual) < 0.001f, message + ". Expected " + expected + ", got " + actual + ".");
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertFalse(bool condition, string message)
        {
            AssertTrue(!condition, message);
        }

        private sealed class FakePoseSource : IPoseSource
        {
            private readonly PoseSample _sample;
            private int _failuresRemaining;
            private bool _connected;
            private string _lastError;

            public FakePoseSource(PoseSample sample)
                : this(sample, 0)
            {
            }

            public FakePoseSource(PoseSample sample, int failuresBeforeSuccess)
            {
                _sample = sample;
                _failuresRemaining = failuresBeforeSuccess;
            }

            public int DisconnectCount { get; private set; }
            public int ConnectCount { get; private set; }

            public bool IsConnected
            {
                get { return _connected; }
            }

            public string LastError
            {
                get { return _lastError; }
            }

            public bool TryConnect(out string error)
            {
                _connected = true;
                ConnectCount++;
                _lastError = null;
                error = null;
                return true;
            }

            public void Disconnect()
            {
                if (_connected)
                {
                    _connected = false;
                    DisconnectCount++;
                }
            }

            public bool TryGetLatest(out PoseSample sample)
            {
                if (_failuresRemaining > 0)
                {
                    _failuresRemaining--;
                    _lastError = "synthetic pose failure";
                    sample = default(PoseSample);
                    return false;
                }

                sample = _sample;
                _lastError = null;
                return _connected;
            }

            public void Dispose()
            {
                Disconnect();
            }
        }
    }
}

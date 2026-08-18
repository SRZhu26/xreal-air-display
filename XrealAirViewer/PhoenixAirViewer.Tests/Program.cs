using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
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

                if (args.Length == 1 && string.Equals(args[0], "--renderer-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunRendererProbe();
                }

                if (args.Length == 1 && string.Equals(args[0], "--session-probe", StringComparison.OrdinalIgnoreCase))
                {
                    return RunSessionProbe();
                }

                TestNormalizeRejectsZero();
                TestFirstSampleRecenters();
                TestRecenterProducesIdentity();
                TestSmoothingUsesTimeConstant();
                TestMaximumAngularVelocity();
                TestHorizonLockRemovesRoll();
                TestRollLockRemovesZTwist();
                TestPanelSettingsValidation();
                TestViewerSettingsPersistence();
                TestViewerLogging();
                TestLatestPoseStore();
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

        private static void TestNormalizeRejectsZero()
        {
            Quaternion normalized;
            AssertFalse(PoseMath.TryNormalize(new Quaternion(0, 0, 0, 0), out normalized), "zero quaternion should be rejected");
        }

        private static void TestFirstSampleRecenters()
        {
            PosePipeline pipeline = CreatePipeline();
            Quaternion output;
            pipeline.TryProcess(Sample(0, Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(70))), out output);
            AssertAngleNear(0.0f, output, "first sample should be neutral");
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
        }

        private static void TestPanelSettingsValidation()
        {
            PanelSettings settings = new PanelSettings
            {
                PanelWidthMeters = 2.0f,
                PanelHeightMeters = 1.125f,
                PanelDistanceMeters = 2.5f,
                CurvatureRadiusMeters = 1.0f
            };
            settings.Validate();
            PanelSettings clone = settings.Clone();
            AssertTrue(Math.Abs(clone.PanelWidthMeters - settings.PanelWidthMeters) < 0.0001f, "panel width should survive cloning");
            AssertTrue(Math.Abs(clone.CurvatureRadiusMeters - settings.CurvatureRadiusMeters) < 0.0001f, "panel curvature should survive cloning");
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
                settings.Panel.PanelWidthMeters = 2.0f;
                settings.Panel.CurvatureRadiusMeters = 2.5f;

                ViewerSettingsStore store = new ViewerSettingsStore(filePath);
                store.Save(settings);
                ViewerSettings loaded = store.Load();
                AssertTrue(loaded.SourceDisplayName == settings.SourceDisplayName, "source display should survive persistence: " + (store.LastLoadError ?? "no load error"));
                AssertTrue(loaded.OutputDisplayName == settings.OutputDisplayName, "output display should survive persistence");
                AssertTrue(loaded.Pose.RollLock, "pose settings should survive persistence");
                AssertTrue(PoseMath.AngularDistanceRadians(loaded.Pose.SensorToRenderer, settings.Pose.SensorToRenderer) < 0.001f, "quaternion mapping should survive persistence");
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
    }
}

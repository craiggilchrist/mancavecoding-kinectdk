using Microsoft.Azure.Kinect.BodyTracking;
using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace ManCaveCoding.KinectDK.Part2
{
	public class KinectViewModel : INotifyPropertyChanged
	{
		#region Events

		public event PropertyChangedEventHandler PropertyChanged;

		#endregion Events

		#region Member vars

		private Device _device;
		private Calibration _calibration;
		private Transformation _transformation;
		private int _colourWidth;
		private int _colourHeight;
		private int _depthWidth;
		private int _depthHeight;
		private SynchronizationContext _uiContext;
		private Tracker _bodyTracker;
		private ImageSource _bitmap;
		private BGRA[] _bodyColours =
		{
			new BGRA(255, 0, 0),
			new BGRA(0, 255, 0),
			new BGRA(0, 0, 255),
			new BGRA(255, 255, 0),
			new BGRA(255, 255, 255),
			new BGRA(0, 255, 255),
			new BGRA(128, 255, 0),
			new BGRA(128, 128, 0),
			new BGRA(128, 128, 128)
		};

		#endregion Member vars

		#region Constructors

		public KinectViewModel()
		{
			Outputs = new ObservableCollection<OutputOption>
			{
				new OutputOption{Name = "Colour", OutputType = OutputType.Colour},
				new OutputOption{Name = "Depth", OutputType = OutputType.Depth},
				new OutputOption{Name = "IR", OutputType = OutputType.IR},
				new OutputOption{Name = "Body tracking", OutputType = OutputType.BodyTracking},
				new OutputOption{Name = "Skeleton tracking", OutputType = OutputType.SkeletonTracking}
			};

			SelectedOutput = Outputs.First();
		}

		#endregion Constructors

		#region VM Properties

		private OutputOption _selectedOutput;
		private bool _applicationIsRunning = true;

		public OutputOption SelectedOutput
		{
			get => _selectedOutput;
			set
			{
				CameraDetails.Clear();
				_selectedOutput = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedOutput"));
			}
		}

		public ObservableCollection<OutputOption> Outputs { get; set; }

		public ObservableCollection<CameraDetailItem> CameraDetails { get; set; } = new ObservableCollection<CameraDetailItem>();


		public ImageSource CurrentCameraImage => _bitmap;

		#endregion VM Properties

		#region Camera control

		internal void StopCamera()
		{
			_applicationIsRunning = false;
			Task.WaitAny(Task.Delay(1000));
			_device.StopImu();
			_device.StopCameras();
			_bodyTracker.Shutdown();

			_bodyTracker.Dispose();
			_device.Dispose();
		}

		internal void StartCamera()
		{
			if (Device.GetInstalledCount() == 0)
			{
				Application.Current.Shutdown();
			}

			_device = Device.Open();

			var configuration = new DeviceConfiguration
			{
				ColorFormat = ImageFormat.ColorBGRA32,
				ColorResolution = ColorResolution.R1080p,
				DepthMode = DepthMode.WFOV_2x2Binned,
				SynchronizedImagesOnly = true,
				CameraFPS = FPS.FPS30
			};

			_device.StartCameras(configuration);

			_device.StartImu();

			_calibration = _device.GetCalibration(configuration.DepthMode, configuration.ColorResolution);
			_transformation = _calibration.CreateTransformation();
			_colourWidth = _calibration.ColorCameraCalibration.ResolutionWidth;
			_colourHeight = _calibration.ColorCameraCalibration.ResolutionHeight;
			_depthWidth = _calibration.DepthCameraCalibration.ResolutionWidth;
			_depthHeight = _calibration.DepthCameraCalibration.ResolutionHeight;

			_uiContext = SynchronizationContext.Current;

			_bodyTracker = Tracker.Create(_calibration, new TrackerConfiguration
			{
				ProcessingMode = TrackerProcessingMode.Gpu,
				SensorOrientation = SensorOrientation.Default
			});

			_bodyTracker.SetTemporalSmooting(1);

			Task.Run(() => { ImuCapture(); });
			Task.Run(() => { CameraCapture(); });
		}

		private void CameraCapture()
		{
			while (_applicationIsRunning)
			{
				try
				{
					using (var capture = _device.GetCapture())
					{
						switch (SelectedOutput.OutputType)
						{
							case OutputType.Depth:
								PresentDepth(capture);
								break;
							case OutputType.IR:
								PresentIR(capture);
								break;
							case OutputType.BodyTracking:
								PresentBodyTracking(capture);
								break;
							case OutputType.SkeletonTracking:
								PresentSkeletonTracking(capture);
								break;
							case OutputType.Colour:
							default:
								PresentColour(capture);
								break;
						}

						PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CurrentCameraImage"));
					}
				}
				catch (Exception ex)
				{
					_applicationIsRunning = false;
					MessageBox.Show($"An error occurred {ex.Message}");
				}
			}

		}

		private void PresentSkeletonTracking(Capture capture)
		{
			_bodyTracker.EnqueueCapture(capture);

			using (var frame = _bodyTracker.PopResult())
			using (Image outputImage = new Image(ImageFormat.ColorBGRA32, _colourWidth, _colourHeight))
			{
				AddOrUpdateDeviceData("Number of bodies", frame.NumberOfBodies.ToString());

				_uiContext.Send(d =>
				{
					// Colour camera pixels.
					var colourBuffer = capture.Color.GetPixels<BGRA>().Span;

					// What we'll output.
					var outputBuffer = outputImage.GetPixels<BGRA>().Span;

					// Create a new image with data from the depth and colour image.
					for (int i = 0; i < colourBuffer.Length; i++)
					{
						// We'll use the colour image if a joint isn't found.
						outputBuffer[i] = colourBuffer[i];
					}

					// Get all of the bodies.
					for (uint b = 0; b < frame.NumberOfBodies && b < _bodyColours.Length; b++)
					{
						var body = frame.GetBody(b);
						var colour = _bodyColours[b];

						foreach (JointId jointType in Enum.GetValues(typeof(JointId)))
						{
							if (jointType == JointId.Count)
							{
								continue; // This isn't really a joint.
							}
							
							var joint = body.Skeleton.GetJoint(jointType);

							AddOrUpdateDeviceData($"Body: {b+1} Joint: {Enum.GetName(typeof(JointId), jointType)}", Enum.GetName(typeof(JointConfidenceLevel), joint.ConfidenceLevel));

							if (joint.ConfidenceLevel >= JointConfidenceLevel.Medium)
							{
								// Get the position in 2d coords.
								var jointPosition = _calibration.TransformTo2D(joint.Position, CalibrationDeviceType.Depth, CalibrationDeviceType.Color);

								if (jointPosition.HasValue)
								{
									// Set a 12x12 pixel value on the buffer.
									var xR = Convert.ToInt32(Math.Round(Convert.ToDecimal(jointPosition.Value.X)));
									var yR = Convert.ToInt32(Math.Round(Convert.ToDecimal(jointPosition.Value.Y)));

									for (int x = xR - 6; x < xR + 7; x++)
									{
										for (int y = yR - 6; y < yR + 7; y++)
										{
											if (x > 0 && x < (outputImage.WidthPixels) && y > 0 && (y < outputImage.HeightPixels))
											{
												outputImage.SetPixel(y, x, colour);
											}
										}
									}

								}
							}
						}
					}

					_bitmap = outputImage.CreateBitmapSource();
					_bitmap.Freeze();
				}, null);
			}
		}

		private void PresentBodyTracking(Capture capture)
		{
			_bodyTracker.EnqueueCapture(capture);

			using (var frame = _bodyTracker.PopResult())
			using (Image outputImage = new Image(ImageFormat.ColorBGRA32, _colourWidth, _colourHeight))
			using (Image transformedDepth = new Image(ImageFormat.Depth16, _colourWidth, _colourHeight, _colourWidth * sizeof(UInt16)))
			using (Image transformedBody = new Image(ImageFormat.Custom8, _colourWidth, _colourHeight, _colourWidth * sizeof(byte)))
			{
				AddOrUpdateDeviceData("Number of bodies", frame.NumberOfBodies.ToString());

				// Transform the depth image & body index to the colour camera perspective.
				_transformation.DepthImageToColorCameraCustom(capture.Depth, frame.BodyIndexMap, transformedDepth, transformedBody, TransformationInterpolationType.Nearest, Frame.BodyIndexMapBackground);

				_uiContext.Send(x =>
				{
					// Get the transformed pixels (colour camera perspective but body pixels).
					var bodyBuffer = transformedBody.GetPixels<byte>().Span;

					// Colour camera pixels.
					var colourBuffer = capture.Color.GetPixels<BGRA>().Span;

					// What we'll output.
					var outputBuffer = outputImage.GetPixels<BGRA>().Span;

					// Create a new image with data from the depth and colour image.
					for (int i = 0; i < colourBuffer.Length; i++)
					{
						// We'll use the colour image if a body isn't tracked (not white 255).
						outputBuffer[i] = colourBuffer[i];
						var bodyIndex = bodyBuffer[i];

						if (bodyIndex != Frame.BodyIndexMapBackground) // ignore background colour
						{
							if (bodyIndex < _bodyColours.Length)
							{
								var colour = _bodyColours[bodyIndex];
								outputBuffer[i].A = colour.A == 0 ? outputBuffer[i].A : colour.A;
								outputBuffer[i].R = colour.R == 0 ? outputBuffer[i].R : colour.R;
								outputBuffer[i].G = colour.G == 0 ? outputBuffer[i].G : colour.G;
								outputBuffer[i].B = colour.B == 0 ? outputBuffer[i].B : colour.B;
							}
						}
					}

					_bitmap = outputImage.CreateBitmapSource();
					_bitmap.Freeze();
				}, null);
			}
		}

		private void PresentColour(Capture capture)
		{
			_uiContext.Send(x =>
			{
				_bitmap = capture.Color.CreateBitmapSource();
				_bitmap.Freeze();
			}, null);
		}

		private void PresentIR(Capture capture)
		{
			_uiContext.Send(x =>
			{
				_bitmap = capture.IR.CreateBitmapSource();
				_bitmap.Freeze();
			}, null);
		}

		private void PresentDepth(Capture capture)
		{
			using (Image outputImage = new Image(ImageFormat.ColorBGRA32, _colourWidth, _colourHeight))
			using (Image transformedDepth = new Image(ImageFormat.Depth16, _colourWidth, _colourHeight, _colourWidth * sizeof(UInt16)))
			{
				// Transform the depth image to the colour capera perspective.
				_transformation.DepthImageToColorCamera(capture, transformedDepth);

				_uiContext.Send(x =>
				{
					// Get the transformed pixels (colour camera perspective but depth pixels).
					Span<ushort> depthBuffer = transformedDepth.GetPixels<ushort>().Span;

					// Colour camera pixels.
					Span<BGRA> colourBuffer = capture.Color.GetPixels<BGRA>().Span;

					// What we'll output.
					Span<BGRA> outputBuffer = outputImage.GetPixels<BGRA>().Span;

					// Create a new image with data from the depth and colour image.
					for (int i = 0; i < colourBuffer.Length; i++)
					{
						// We'll use the colour image if the depth is less than 1 metre. 
						outputBuffer[i] = colourBuffer[i];
						var depth = depthBuffer[i];

						if (depth == 0) // No depth image.
						{
							outputBuffer[i].R = 0;
							outputBuffer[i].G = 0;
							outputBuffer[i].B = 0;
						}

						if (depth >= 1000 && depth < 1200) // More than a meter away.
						{
							outputBuffer[i].R = Convert.ToByte(255 - (255 / (depth - 999)));
						}

						if (depth >= 1200 && depth < 1500)
						{
							outputBuffer[i].G = Convert.ToByte(255 - (255 / (depth - 1199)));
						}

						if (depth >= 1500 && depth < 2000)
						{
							outputBuffer[i].B = Convert.ToByte(255 - (255 / (depth - 1499)));
						}

						if (depth >= 2000)
						{
							outputBuffer[i].Value = 0;
						}
					}

					_bitmap = outputImage.CreateBitmapSource();
					_bitmap.Freeze();
				}, null);
			}
		}

		private void ImuCapture()
		{
			while (_applicationIsRunning)
			{
				try
				{
					var imu = _device.GetImuSample();

					AddOrUpdateDeviceData("Accelerometer: Timestamp", imu.AccelerometerTimestamp.ToString(@"hh\:mm\:ss"));
					AddOrUpdateDeviceData("Accelerometer: X", (Math.Round(imu.AccelerometerSample.X, 1)).ToString());
					AddOrUpdateDeviceData("Accelerometer: Y", (Math.Round(imu.AccelerometerSample.Y, 1)).ToString());
					AddOrUpdateDeviceData("Accelerometer: Z", (Math.Round(imu.AccelerometerSample.Z, 1)).ToString());
					AddOrUpdateDeviceData("Gyro: Timestamp", imu.GyroTimestamp.ToString(@"hh\:mm\:ss"));
					AddOrUpdateDeviceData("Gyro: X", (Math.Round(imu.GyroSample.X, 0)).ToString());
					AddOrUpdateDeviceData("Gyro: Y", (Math.Round(imu.GyroSample.Y, 0)).ToString());
					AddOrUpdateDeviceData("Gyro: Z", (Math.Round(imu.GyroSample.Z, 0)).ToString());
					AddOrUpdateDeviceData("Temperature: ", (Math.Round(imu.Temperature, 1)).ToString());
				}
				catch (Exception ex)
				{
					_applicationIsRunning = false;
					MessageBox.Show($"An error occurred {ex.Message}");
				}
			}
		}

		private void AddOrUpdateDeviceData(string key, string value)
		{
			var detail = CameraDetails.FirstOrDefault(i => i.Name == key);

			if (detail == null)
			{
				detail = new CameraDetailItem { Name = key, Value = value };

				_uiContext.Send(x => CameraDetails.Add(detail), null);
			}

			detail.Value = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CameraDetails"));
		}

		#endregion Camera control
	}

	public enum OutputType
	{
		Colour,
		Depth,
		IR,
		BodyTracking,
		SkeletonTracking
	}

	public class OutputOption
	{
		public string Name { get; set; }

		public OutputType OutputType { get; set; }
	}
}
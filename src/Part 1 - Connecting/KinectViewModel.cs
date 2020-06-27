using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ManCaveCoding.KinectDK.Part1
{
	public class KinectViewModel : INotifyPropertyChanged
	{
		#region Events

		public event PropertyChangedEventHandler PropertyChanged;

		#endregion Events

		#region Member vars

		private Device _device;
		private Transformation _transformation;
		private int _colourWidth;
		private int _colourHeight;
		private int _depthWidth;
		private int _depthHeight;
		private SynchronizationContext _uiContext;
		private ImageSource _bitmap;

		#endregion Member vars

		#region Constructors

		public KinectViewModel()
		{
			Outputs = new ObservableCollection<OutputOption>
			{
				new OutputOption{Name = "Colour", OutputType = OutputType.Colour},
				new OutputOption{Name = "Depth", OutputType = OutputType.Depth},
				new OutputOption{Name = "IR", OutputType = OutputType.IR}
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

			var calibration = _device.GetCalibration(configuration.DepthMode, configuration.ColorResolution);
			_transformation = calibration.CreateTransformation();
			_colourWidth = calibration.ColorCameraCalibration.ResolutionWidth;
			_colourHeight = calibration.ColorCameraCalibration.ResolutionHeight;
			_depthWidth = calibration.DepthCameraCalibration.ResolutionWidth;
			_depthHeight = calibration.DepthCameraCalibration.ResolutionHeight;

			_uiContext = SynchronizationContext.Current;

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
		IR
	}

	public class OutputOption
	{
		public string Name { get; set; }

		public OutputType OutputType { get; set; }
	}
}
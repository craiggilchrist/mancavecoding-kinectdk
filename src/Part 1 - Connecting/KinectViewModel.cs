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
		private Transformation _transform;
		private int _colourWidth;
		private int _colourHeight;
		private int _depthWidth;
		private int _depthHeight;
		private SynchronizationContext _uiContext;
		private WriteableBitmap _depthBitmap;
		private WriteableBitmap _irBitmap;
		private ImageSource _colourBitmap;

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


		public ImageSource CurrentCameraImage
		{
			get
			{
				switch (SelectedOutput.OutputType)
				{
					case OutputType.Depth:
						return _depthBitmap;
					case OutputType.IR:
						return _irBitmap;
					default:
					case OutputType.Colour:
						return _colourBitmap;
				}
			}
		}

		#endregion VM Properties

		#region Camera control

		internal void StopCamera()
		{
			_applicationIsRunning = false;
			Task.WaitAny(Task.Delay(1000));
			_device.StopImu();
			_device.StopCameras();
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
				CameraFPS = FPS.FPS15,
				ColorFormat = ImageFormat.ColorMJPG,
				ColorResolution = ColorResolution.R720p,
				DepthMode = DepthMode.NFOV_Unbinned,
				SynchronizedImagesOnly = true
			};

			_device.StartCameras(configuration);

			_device.StartImu();

			var calibration = _device.GetCalibration(configuration.DepthMode, configuration.ColorResolution);
			_transform = calibration.CreateTransformation();
			_colourWidth = calibration.ColorCameraCalibration.ResolutionWidth;
			_colourHeight = calibration.ColorCameraCalibration.ResolutionHeight;
			_depthWidth = calibration.DepthCameraCalibration.ResolutionWidth;
			_depthHeight = calibration.DepthCameraCalibration.ResolutionHeight;

			_uiContext = SynchronizationContext.Current;

			_depthBitmap = new WriteableBitmap(_colourWidth, _colourHeight, 96.0, 96.0, PixelFormats.Bgr565, BitmapPalettes.BlackAndWhiteTransparent);
			_irBitmap = new WriteableBitmap(_depthWidth, _depthHeight, 192.0, 192.0, PixelFormats.Bgr555, null);

			Task.Run(() => { ImuCapture(); });
			Task.Run(() => { CameraCapture(); });
		}

		private void CameraCapture()
		{
			while (_applicationIsRunning)
			{
				try
				{
					using (Image transformedDepth = new Image(ImageFormat.Depth16, _colourWidth, _colourHeight, _colourWidth * sizeof(UInt16)))
					using (var capture = _device.GetCapture())
					{
						switch (SelectedOutput.OutputType)
						{
							case OutputType.Depth:
								_transform.DepthImageToColorCamera(capture, transformedDepth);
								_uiContext.Send(x =>
								{
									_depthBitmap.Lock();

									Image color = transformedDepth;

									var region = new Int32Rect(0, 0, color.WidthPixels, color.HeightPixels);

									unsafe
									{
										using (var pin = color.Memory.Pin())
										{
											_depthBitmap.WritePixels(region, (IntPtr)pin.Pointer, (int)color.Size, color.StrideBytes);
										}
									}

									_depthBitmap.AddDirtyRect(region);
									_depthBitmap.Unlock();
								}, null);
								break;
							case OutputType.IR:
								using (var ms = new MemoryStream(capture.IR.Memory.ToArray()))
								{
									_uiContext.Send(x =>
									{
										_irBitmap.Lock();

										var color = capture.IR;
										var region = new Int32Rect(0, 0, color.WidthPixels, color.HeightPixels);

										unsafe
										{
											using (var pin = color.Memory.Pin())
											{
												_irBitmap.WritePixels(region, (IntPtr)pin.Pointer, (int)color.Size, color.StrideBytes);
											}
										}

										_irBitmap.AddDirtyRect(region);
										_irBitmap.Unlock();
									}, null);
								}
								break;
							case OutputType.Colour:
							default:
								using (var ms = new MemoryStream(capture.Color.Memory.ToArray()))
								{
									_uiContext.Send(x =>
									{
										var bitmap = new System.Drawing.Bitmap(ms);

										var bitmapData = bitmap.LockBits(
											new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
											System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

										var bitmapSource = BitmapSource.Create(
											bitmapData.Width, bitmapData.Height,
											bitmap.HorizontalResolution, bitmap.VerticalResolution,
											PixelFormats.Bgr24, null,
											bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

										bitmap.UnlockBits(bitmapData);

										bitmapSource.Freeze();

										_colourBitmap = bitmapSource;
									}, null);
								}
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
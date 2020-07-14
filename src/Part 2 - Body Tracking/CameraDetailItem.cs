using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace ManCaveCoding.KinectDK.Part2
{
	/// <summary>
	/// Represents a single value from IMU or device property.
	/// </summary>
	public class CameraDetailItem : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public string Name { get; set; }

		private string _value;

		public string Value
		{
			get => _value; set
			{
				_value = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Value"));
			}
		}
	}
}

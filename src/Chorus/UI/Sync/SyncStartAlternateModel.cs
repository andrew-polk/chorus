﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chorus.VcsDrivers.Mercurial;

namespace Chorus.UI.Sync
{
	class SyncStartAlternateModel : ISyncStartModel
	{
		public SyncStartAlternateModel(HgRepository repository)
		{
			throw new NotImplementedException();
		}

		public bool GetInternetStatusLink(out string buttonLabel, out string message, out string tooltip)
		{
			throw new NotImplementedException();
		}

		public bool GetNetworkStatusLink(out string message, out string tooltip)
		{
			throw new NotImplementedException();
		}

		public bool GetUsbStatusLink(IUsbDriveLocator usbDriveLocator, out string message)
		{
			throw new NotImplementedException();
		}

		public void SetNewSharedNetworkAddress(string path)
		{
			throw new NotImplementedException();
		}
	}
}
﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace NAPS2.Update
{
    public interface IAutoUpdaterClient
    {
        void UpdateAvailable(VersionInfo versionInfo);
        void InstallComplete();
    }
}

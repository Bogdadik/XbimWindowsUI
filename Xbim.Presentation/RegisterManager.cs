using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.LocalizationHelper;

namespace Xbim.Presentation
{
    public static class RegisterManager
    {
        public static void Register()
        {
            ResourceManagerService.RegisterManager("XbimPresentationResource", XbimPresentation.ResourceManager, true);
        }

        public static string L(string managerName, string resourceKey)
        {
            var value = ResourceManagerService.GetResourceString(managerName, resourceKey);
            return string.IsNullOrWhiteSpace(value) ? resourceKey : value;
        }

        public static string L(string resourceKey)
        {
            var value = ResourceManagerService.GetResourceString("XbimPresentationResource", resourceKey);
            return string.IsNullOrWhiteSpace(value) ? resourceKey : value;
        }
    }
}

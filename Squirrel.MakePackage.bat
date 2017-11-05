@set "version=4.0.12-V0044"
@echo Ensure an updated version of the squirrel repo in Squirrel.Windows
@rmdir OutPut\Release /s /q
@echo Build the solution in release mode now.
@echo. 
@pause
@"C:\Program Files (x86)\NuGet\nuget.exe" pack "Xbim.Xplorer.squirrel.nuspec" -Version %version%
@echo Releasifying
@"Packages\squirrel.windows.1.5.3-cb003\tools\Squirrel.exe" --releasify Xbim.Xplorer.%version%.nupkg --releaseDir=..\Squirrel.Windows\XplorerReleases --no-msi
@del Xbim.Xplorer.%version%.nupkg
@pause
@echo off
cls

@"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBUILD.exe" RestorePackages.targets /target:RestoreBuildPackages

set PathToBuildReport=.\..\Packages\BuildReport.1.0.0
xcopy /I /Y %PathToBuildReport%\_BuildReport_Files .\_BuildReport_Files

set CONFIGURATION=Debug

set xml=Build_Report_%CONFIGURATION%_Setup.xml
set html=Build_Report_%CONFIGURATION%_Setup.html

set logger=/l:XmlFileLogger,"%PathToBuildReport%\MSBuild.ExtensionPack.Loggers.dll";logfile=%xml%
"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBUILD.exe" /m Build.proj %logger% /property:OneStepOnly=true;BuildSetup=true;Configuration=%CONFIGURATION% 
echo Component build exit code %errorlevel%
if errorlevel 1 (
   echo Component build failed
   exit /b %errorlevel%
)

%PathToBuildReport%\msxsl %xml% _BuildReport_Files\BuildReport.xslt -o %html%

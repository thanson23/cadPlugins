dotnet build -c Release
dotnet tool run obfuscar.console obfuscar.xml
mv -Force obfuscated/Tolerance.Plugin.dll bin/Release/Tolerance.Plugin.dll
mv -Force obfuscated/AutoCAD.Utils.dll bin/Release/AutoCAD.Utils.dll
rm -Recurse obfuscated/
dotnet build -c Release ../GradeSlope/
cp ../GradeSlope/bin/Release/grade-slope.exe bin/Release/grade-slope.exe

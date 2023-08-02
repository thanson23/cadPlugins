Stop-Process -Name acad
dotnet build
& "C:\Program Files\Autodesk\AutoCAD 2023\acad.exe" `
    /ld "C:\Program Files\Autodesk\AutoCAD 2023\AecBase.dbx" `
    /p "<<C3D_Imperial>>" `
    /product C3D `
    /language en-US `
    /nohardware `
    /b Z:\src\Plugin\test.scr

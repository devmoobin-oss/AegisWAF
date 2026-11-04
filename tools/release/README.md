# Release provenance manifest

สร้าง manifest ที่ไม่บรรจุเนื้อหาไฟล์หรือ secret แต่บันทึก SHA-256 และขนาดของทุกไฟล์ใน publish bundle พร้อม commit/build metadata:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Write-ReleaseManifest.ps1 `
  -InputDirectory .\dist\AegisWAF-Desktop `
  -OutputPath .\dist\AegisWAF-Desktop\release-manifest.json `
  -RepositoryRoot . `
  -BuildCommand 'dotnet publish src/AegisWAF.Desktop/AegisWAF.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true' `
  -TargetFramework net10.0-windows `
  -RuntimeIdentifier win-x64
```

ค่า `signed=false` เป็นการระบุอย่างตรงไปตรงมาว่ายังไม่ได้เซ็นดิจิทัล ต้องตั้งค่า certificate/signing service ที่ได้รับอนุมัติก่อนจึงจะอ้างว่า signed ได้

ทดสอบตัว writer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\test-release-manifest.ps1
```

ตรวจสอบ bundle หลัง publish ว่าทุกไฟล์มีอยู่จริง ขนาดและ SHA-256 ตรงกับ manifest และไม่มีไฟล์แปลกปลอม:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Verify-ReleaseManifest.ps1 `
  -InputDirectory .\dist\AegisWAF-Desktop `
  -ManifestPath .\dist\AegisWAF-Desktop\release-manifest.json `
  -Json
```

ทดสอบ verifier รวมทั้งกรณีไฟล์ถูกแก้ไขและมีไฟล์เกิน:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\test-release-verifier.ps1
```

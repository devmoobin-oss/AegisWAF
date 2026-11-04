# Origin exposure verification

ใช้ตรวจว่า Next.js origin ที่ AegisWAF proxy อยู่ไม่ได้เปิดรับจาก LAN/public interface โดยตรง

เริ่ม Next.js แบบ loopback:

```powershell
npx next start -H 127.0.0.1 -p 4000
```

ตรวจ listener:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\security\Assert-LoopbackOrigin.ps1 -Port 4000 -Json
```

ผลที่ผ่านต้องมี listener เฉพาะ `127.0.0.1` หรือ `::1` เท่านั้น ถ้าเป็น `0.0.0.0`, `::` หรือ IP ของ LAN จะ fail ทันที ส่วนไม่มี listener ก็ fail เพราะยังพิสูจน์ไม่ได้ว่า origin พร้อมใช้งาน

รันทดสอบ deterministic โดยไม่แตะ network:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\security\test-loopback-origin.ps1
```

การตรวจนี้เป็น deployment guardrail ไม่ใช่ WAF inspection และไม่ทดแทน firewall, network ACL, service isolation หรือ penetration test

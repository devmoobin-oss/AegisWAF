# การป้องกัน Next.js origin ไม่ให้เข้าตรง

เมื่อ public hostname เช่น `affiliateiq.localhost` ชี้เข้า AegisWAF แล้ว traffic จะถูกตรวจ WAF ก่อนส่งต่อไปยัง Next.js แต่ AegisWAF ไม่สามารถเปลี่ยน bind address ของ process Next.js ที่แยกออกมาได้

## คำสั่งที่แนะนำ

หลัง build Next.js แล้ว ให้รัน origin ด้วย loopback เท่านั้น:

```powershell
npx next start -H 127.0.0.1 -p 4000
```

หรือกำหนดใน `package.json`:

```json
{
  "scripts": {
    "start:waf": "next start -H 127.0.0.1 -p 4000"
  }
}
```

จากนั้นตรวจสอบ:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\AegisWAF\tools\security\Assert-LoopbackOrigin.ps1 -Port 4000 -Json
```

## ค่าที่ใช้ใน Multi-Site

- Hostname ที่ผู้ใช้เข้า: `affiliateiq.localhost` (ไม่ใส่ port)
- Upstream สำหรับ development: `http://127.0.0.1:4000/`
- Port ที่เปิดให้ผู้ใช้เข้า: port ของ AegisWAF เช่น `5080` หรือ port ที่ตั้งไว้

ผู้ใช้ควรเข้า `http://affiliateiq.localhost:5080/` ผ่าน Gateway ไม่ใช่ `http://localhost:4000/` โดยตรง ถ้าต้องการใช้ port 80 ให้ตั้ง listener ของ AegisWAF เป็น 80 และตรวจสิทธิ์ Windows/firewall เพิ่มเติม

## ข้อจำกัด

การ bind ที่ loopback ป้องกันการรับ connection จากเครื่องอื่นในระดับ listener แต่ยังไม่ใช่ firewall policy และไม่ป้องกัน process อื่นบนเครื่องเดียวกัน การใช้งาน Production ควรใช้ HTTPS, firewall/network ACL และทดสอบจาก authorized staging environment เพิ่มเติม

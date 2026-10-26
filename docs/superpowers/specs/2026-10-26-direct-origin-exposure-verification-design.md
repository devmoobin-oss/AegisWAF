# Direct origin exposure verification — design

## Goal

ทำให้การ deploy Next.js หลัง AegisWAF ตรวจสอบได้ว่า origin เปิดรับเฉพาะ loopback และไม่เปิดพอร์ต origin บน LAN/public interface โดยไม่อ้างว่า Gateway สามารถเปลี่ยน bind ของ process อื่นได้

## Boundary

- AegisWAF ยังคงทำหน้าที่ inspect และ proxy traffic ที่เข้า Gateway
- Next.js ต้อง bind เองด้วย `127.0.0.1` หรือ `::1`
- verifier จะอ่าน listener state ของเครื่อง หรือ deterministic JSON snapshot ใน automated tests
- wildcard (`0.0.0.0`, `::`) และ non-loopback address เป็น failure
- ไม่มี listener ก็เป็น failure เพราะไม่สามารถพิสูจน์ว่า origin พร้อมและปลอดภัย

## Output contract

Verifier คืน JSON ที่มี `passed`, `port`, `listeners`, `findings`, `command` และ `checkedAt` พร้อม exit code 0 เมื่อผ่าน และ 1 เมื่อไม่ผ่าน เพื่อใช้ใน release/deployment gate

## Test-first gates

1. loopback IPv4/IPv6 listeners ผ่าน
2. wildcard listener ไม่ผ่าน
3. LAN/non-loopback listener ไม่ผ่าน
4. ไม่มี listener ไม่ผ่าน
5. self-test และ repository contract test ต้องผ่าน

## Explicit limitation

การตรวจนี้ไม่ทดแทน firewall, network ACL, container/service isolation หรือ external penetration test และไม่ตรวจว่า application มี vulnerability ภายในหรือไม่

# Identity Hardening Design

## Intent

ปิดช่องว่างของ local authentication fallback ที่ปัจจุบัน throttle ตามคู่ `source IP + username` เป็นหลัก ทำให้ผู้โจมตีจาก IP เดียวสามารถกระจายลองหลาย username ได้มากกว่าที่ policy ตั้งใจไว้

## Goals

- เพิ่ม bounded failure buckets แยกตาม source IP, username และคู่ IP+username ใน `AuthThrottle`
- ให้ local/Desktop fallback ปฏิเสธการลองซ้ำจาก IP เดียวข้ามหลาย username ได้
- คง shared PostgreSQL throttle ที่มี key แยก IP และ username อยู่แล้ว
- เพิ่ม regression tests และปรับเอกสารให้ตรงกับ behavior จริง
- ไม่เปลี่ยน password policy ที่มีอยู่และไม่อ้างว่า MFA/WebAuthn/OIDC เสร็จ

## Non-goals

- ไม่ implement MFA, TOTP, WebAuthn หรือ OIDC ในโมดูลนี้
- ไม่เพิ่ม dependency ภายนอก
- ไม่เปลี่ยน schema หรือ protocol ของ distributed throttle

## Chosen approach

เพิ่ม key namespaces (`ip:`, `user:`, `pair:`) ใน in-memory throttle เดิม ใช้ failure threshold/window เดียวกันทุก bucket และ reset IP/username/pair เมื่อ login สำเร็จ เพื่อไม่ให้ burst ชั่วคราวทำให้ผู้ใช้ที่ยืนยันตัวตนสำเร็จถูกกักค้าง

## Security invariants

- ผู้โจมตีจาก IP เดียวกันที่กระจาย username ต้องชน IP bucket ภายใน threshold เดียวกัน
- Username เดียวที่ถูกโจมตีจากหลาย IP ต้องชน username bucket
- การ hash/normalize key ต้องไม่เปิดเผยว่า username มีอยู่จริง
- จำนวน bucket ต้องยัง bounded และ expired entries ถูก prune


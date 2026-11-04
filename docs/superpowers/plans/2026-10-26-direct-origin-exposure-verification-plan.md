# Direct origin exposure verification — implementation plan

## Task 1 — RED contract

- เพิ่ม contract test ที่ต้องพบ verifier, self-test, คู่มือ Next.js และ workflow gate
- เพิ่ม self-test cases สำหรับ loopback, wildcard, LAN address และไม่มี listener

## Task 2 — Implement verifier

- เพิ่ม `tools/security/Assert-LoopbackOrigin.ps1`
- รองรับ Windows `Get-NetTCPConnection` และ fallback `netstat -ano`
- รองรับ snapshot JSON เพื่อทดสอบโดยไม่แตะ network จริง
- คืน JSON และ exit code ที่เหมาะกับ CI

## Task 3 — Operator guidance

- เพิ่มคู่มือภาษาไทยพร้อมคำสั่ง `next start -H 127.0.0.1 -p 4000`
- อธิบายว่า hostname public ผ่าน WAF ไม่ใช่การเปิด origin port

## Task 4 — CI and verification

- เพิ่ม Windows workflow job สำหรับ self-test
- รัน self-test, contract tests, solution build และ `git diff --check`
- บันทึก Before/After/Target และข้อจำกัดใน `handoff.md`

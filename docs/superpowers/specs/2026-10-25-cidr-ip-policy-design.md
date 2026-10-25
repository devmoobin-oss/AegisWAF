# CIDR-aware IP Policy Design

## Intent

เพิ่มความสามารถของ IP policy ให้ operator กำหนด allow/block เป็นทั้ง IP เดี่ยวและ network แบบ CIDR ได้ โดยยังคงหลักการสำคัญว่า `allow` เป็นเพียงข้อยกเว้นจาก blocklist เท่านั้น และไม่ข้าม WAF inspection หรือ rate limiting

## Goals

- รับ IPv4/IPv6 เดี่ยวและ CIDR ที่ถูกต้อง พร้อม normalize เป็น key เดียวกัน
- เลือก policy ที่เฉพาะเจาะจงที่สุดเมื่อมี network ซ้อนกัน; exact IP ชนะ CIDR เสมอ
- ทำให้ policy ที่บันทึกใน PostgreSQL และ policy ที่โหลดเข้า memory ใช้ key เดียวกัน
- รองรับการลบ CIDR ผ่าน query endpoint โดยคง route เดิมสำหรับ IP เดี่ยวไว้
- ปรับ Desktop validation และเอกสารให้ตรงกับพฤติกรรมจริง
- มี regression tests สำหรับ matching, precedence, normalization, API persistence/deletion และการที่ allow ยังถูกตรวจ WAF/rate limit

## Non-goals

- GeoIP, threat-intelligence feed, dynamic DNS/CIDR feed หรือ wildcard ที่ไม่ใช่ CIDR
- เปลี่ยนลำดับการตัดสินใจของ blocklist, inspection หรือ rate limiter
- ทำให้ `allow` เป็น trusted bypass

## Approaches

1. **คง exact IP เท่านั้น** — เปลี่ยนน้อยที่สุด แต่ไม่ปิด gap เรื่อง subnet policy และไม่ตอบโจทย์การบริหาร network
2. **ใช้ string wildcard/prefix matching** — เขียนง่ายแต่ไม่เข้าใจ IPv4/IPv6 boundary, prefix length และอาจ match ข้าม network อย่างไม่ปลอดภัย
3. **ใช้ `IPAddress` + prefix mask ที่มีอยู่ใน `Cidr` utility (เลือกใช้)** — reuse logic ที่ผ่านการทดสอบแล้ว, normalize network bits, เลือก longest-prefix match ได้ deterministic และไม่เพิ่ม dependency

## Security decisions

- Input ที่ไม่มี slash คือ exact IP; input ที่มี slash ต้องมี prefix อยู่ในช่วงของ address family
- Network bits จะถูก mask ก่อนเก็บ เช่น `192.0.2.44/24` กลายเป็น `192.0.2.0/24`
- Exact match ชนะ CIDR ทุกกรณี; ถ้าเป็น CIDR หลายรายการให้ใช้ prefix ที่ยาวที่สุด
- Policy `allow` ไม่ข้าม signature inspection และ rate limit; policy `block` ปฏิเสธทุก request ตาม behavior เดิม
- endpoint ลบแบบ `/api/ip-policies/{ip}` ยังรองรับ exact IP และเพิ่ม `DELETE /api/ip-policies?ip=<policy>` สำหรับ CIDR

## Constraints

- ต้องรักษา backward compatibility ของ client ที่ใช้ exact-IP route เดิม
- ต้องทำ test-first ก่อนแก้ production code
- ต้องวัดโมดูลใหม่ด้วย build, focused tests, integration/API contract และ documentation gate; เป้าหมายหลังรอบต้องมากกว่า 98%

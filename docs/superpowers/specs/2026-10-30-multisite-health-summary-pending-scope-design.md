# Multi-Site health summary pending count scope

## Intent

ทำให้ข้อความของ Browser/Desktop ไม่สื่อว่า `pendingRestartSites` เป็นจำนวนรวมทั้งหมด เมื่อ Health Summary ใช้หน้าข้อมูลแบบ bounded page และนับได้เฉพาะ Site ที่ถูกส่งกลับมา

## Goals

- ใช้ `countsScope` เดิมเป็นแหล่งความจริงของขอบเขต Summary
- เมื่อ `countsScope=returned-sites` ให้ระบุในข้อความภาษาไทยว่าเป็นจำนวน “ในรายการนี้”
- เมื่อ Summary ไม่ถูกตัดหน้า ให้คงข้อความสั้นเดิม “รอเปิดใช้ N Site”
- ไม่เปลี่ยน API query limit, health calculation, pending classification หรือ alert counts

## Non-goals

- ไม่เพิ่ม unbounded database query เพื่อคำนวณ pending Site ทั้งหมด
- ไม่เปลี่ยนความหมายของ `pendingRestartSites`: เมื่อ page ถูกตัด จะนับเฉพาะรายการที่ส่งกลับ
- ไม่ activate, mutate, delete หรือ restart Gateway

## Verification boundary

ใช้ Browser/Desktop contract tests, targeted Multi-Site regression, JavaScript syntax check, Release build, diff check และ publish manifest verification

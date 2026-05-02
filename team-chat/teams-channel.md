# #foundation-team — Slack 頻道紀錄

---

**Arthur (後端 Lead)** — 昨天 09:15
> 跟大家更新一下：昨天 ACME 客戶回報了一個權限問題。他們有個員工角色的人登入考勤模組，看到了全公司 342 個部門和 1,247 名員工。照理只應該看到自己部門。
>
> 我查了一下 log：
> ```
> 14:00:01 User=alice@acme.com | Role=Employee | GET /api/departments → 342 records
> 14:00:02 User=alice@acme.com | Role=Employee | GET /api/employees/NameFind → 1,247 records
> 14:02:15 User=hr_admin@acme.com | Role=Hr | GET /api/departments → 15 records
> 14:05:30 User=bob_mgr@acme.com | Role=Manager | GET /api/departments → 342 records
> 14:08:00 User=carol_sec@acme.com | Role=Secretary | GET /api/departments → 342 records
> ```
>
> HR 只看到 15 筆是正確的。但 Employee、Manager、Secretary 都看到 342 筆。感覺不只是單一 bug。

**Cathy (PM)** — 昨天 10:30
> 這個客戶很重視資安。我們需要：
> 1. 根因分析
> 2. 客戶溝通信（我來發，但需要你們提供技術摘要，不要給太多技術細節）
> 3. 修復方案 + 時程
>
> 另外，我需要知道前端修改會影響到哪些角色的哪些功能頁面，我才能判斷修改方式是否符合商業邏輯。

**Kevin (Junior)** — 昨天 14:00
> 我提了一個 PR #87（試用期轉正功能），可以幫我 review 嗎？我在本機 Postman 測過了。
> 檔案在 `codebase/PR-pending-convert-fulltime.cs`

**Arthur** — 昨天 14:15
> @Kevin 收到，會排 review。
> 不過大家手上都卡著 ACME 的權限問題和下週的匯入功能，看看新來的同事能不能幫忙 review？

**Emily (QA)** — 昨天 16:00
> 提醒一下，上週有客戶反映大量匯入員工時很慢（3,000 筆花了 8 分鐘），而且中間有幾筆失敗後，已成功的資料不知道會不會受影響。我開了 BACKLOG-003。

**Arthur** — 今天 08:30
> @新同事 歡迎加入！你的 onboarding 資料都在 repo 裡了。有問題隨時問。我們沒有規定你第一天一定要做什麼，但如果你能幫上忙處理上面任何一件事，那就太好了。

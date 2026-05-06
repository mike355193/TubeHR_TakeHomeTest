// ══════════════════════════════════════════════════════════════
// PR #87: 新增員工試用期轉正端點
// 作者: Kevin (Junior, 入職 2 個月)
// 狀態: Open — 等待 Review
// ══════════════════════════════════════════════════════════════

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace TubeHR.Foundation.Controllers
{
    public partial class EmployeeController : ControllerBase
    {
        private readonly FoundationDbContext _context;
        private readonly HttpClient _httpClient;

        [HttpPost("{employeeId}/convert-fulltime")]
        public async Task<IActionResult> ConvertToFullTime(Guid employeeId)
        {
            try
            {
                var employee = await _context.Employees.FindAsync(employeeId);

                if (employee == null)
                    return NotFound("找不到員工");

                employee.StatusCode = "A01";
                employee.ContractType = "FullTime";
                employee.ModifyOn = DateTime.Now;
                employee.ModifyBy = GetCurrentUserId();

                await _context.SaveChangesAsync();

                // 同步到考勤系統
                var payload = JsonSerializer.Serialize(employee);
                var res1 = await _httpClient.PostAsync(
                    "https://attendance-api.internal/api/sync/employee",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                if (!res1.IsSuccessStatusCode)
                    Console.WriteLine($"考勤同步失敗：{employeeId}");

                // 同步到薪資系統
                var res2 = await _httpClient.PostAsync(
                    "https://payroll-api.internal/api/sync/employee",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                if (!res2.IsSuccessStatusCode)
                    Console.WriteLine($"薪資同步失敗：{employeeId}");

                return Ok(new { Message = "員工已轉為正式", EmployeeId = employeeId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"轉正失敗 {employeeId}: {ex}");
                return StatusCode(500, "發生錯誤");
            }
        }

        private Guid GetCurrentUserId() => Guid.Empty; // simplified
    }
}


/*
 * 應該要同步到考勤系統以及薪資系統之後，這名員工才能被標記為正式轉正
 * 所以應該是做完同步考勤以及薪資系統後才能把資料update到DB裡面，這樣才不會造成資料不一致的問題，SaveChangesAsync的行為在最後才執行
 * 如果同步考勤系統失敗就應該直接回傳失敗了，不該再繼續往下走；同樣的同步薪資系統假設失敗了也應該回傳失敗，不該再繼續往下走
 * 又再來假設同步考勤系統成功了，但同步薪資系統失敗了，這時候就會造成資料不一致的問題
 * 所以應該再設計個補償機制，當同步薪資系統失敗了，就要去呼叫考勤系統的 API 把剛剛同步的資料刪掉，這樣就不會造成資料不一致的問題
 * 又假設很不幸的是，補償機制也失敗了，這時候就只能記錄 log，並且人工去處理了
 * 
 * 再來如果最後的儲存DB資料也失敗了
 * 那同理不僅考勤要恢復、薪資同步也要恢復
 * 同理補償機制都失敗了，那就只能記錄 log，並且人工去處理了
 */
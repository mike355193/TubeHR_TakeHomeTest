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

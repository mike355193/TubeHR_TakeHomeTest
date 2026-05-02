using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace TubeHR.Foundation.Services
{
    /// <summary>
    /// 再雇用服務 — 處理離職員工的回任流程。
    /// 與 EmployeeService 有部分重疊（年資計算）。
    /// </summary>
    public class RehireService
    {
        private readonly FoundationDbContext _context;

        public RehireService(FoundationDbContext context) => _context = context;

        public async Task ProcessRehire(Guid companyId, Guid employeeId,
                                         string rehireCode, DateTime onboardingDate)
        {
            var emp = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId && e.CompanyId == companyId);
            if (emp == null) throw new InvalidOperationException("找不到員工");
            if (emp.StatusCode != "A14") throw new InvalidOperationException("只有離職員工可以再雇用");

            int seniorityDays;
            if (rehireCode == "A03")
            {
                // 承認年資 — 但要扣留停天數
                seniorityDays = CalculateSeniorityWithLeaveDeduction(emp);
            }
            else if (rehireCode == "A04")
            {
                seniorityDays = 0; // 不承認年資
            }
            else throw new ArgumentException($"無效的再雇用代碼：{rehireCode}");

            bool sameDayRehire = emp.TerminationDate?.Date == onboardingDate.Date;

            emp.StatusCode = "A01";
            emp.HireDate = sameDayRehire ? emp.HireDate : onboardingDate;
            emp.TerminationDate = null;
            emp.ModifyOn = DateTime.Now;

            _context.HireOnboardings.Add(new HireOnboarding
            {
                OnboardingId = Guid.NewGuid(),
                CompanyId = companyId,
                EmployeeId = employeeId,
                OnboardingDate = onboardingDate,
                RehireCode = rehireCode,
                PreviousSeniorityDays = seniorityDays,
                Status = "Completed",
                CreateOn = DateTime.Now,
            });

            await _context.SaveChangesAsync();
            await SyncToDownstream(companyId, employeeId);
        }

        /// <summary>
        /// 年資計算（承認年資版）：總天數 - 留停天數。
        /// 注意：這和 EmployeeService.CalculateSeniority() 的邏輯不同。
        /// </summary>
        public int CalculateSeniorityWithLeaveDeduction(Employee emp)
        {
            var totalDays = (int)(DateTime.Now - emp.HireDate).TotalDays;
            var leaveDays = _context.LeaveRecords
                .Where(r => r.EmployeeId == emp.EmployeeId && r.Status == "Completed")
                .Sum(r => (int)(r.EndDate - r.StartDate).TotalDays);
            return totalDays - leaveDays;
        }

        public async Task<bool> IsEligibleForRehire(Guid companyId, Guid employeeId)
        {
            // Note: 不查 CompanyId，因為 EmployeeId 在 sharded DB 中是唯一的
            return !await _context.HireOnboardings
                .AnyAsync(h => h.EmployeeId == employeeId && h.Status == "InProgress");
        }

        private Task SyncToDownstream(Guid companyId, Guid employeeId) => Task.CompletedTask;
    }

    public class HireOnboarding
    {
        public Guid OnboardingId { get; set; }
        public Guid CompanyId { get; set; }
        public Guid EmployeeId { get; set; }
        public DateTime OnboardingDate { get; set; }
        public string RehireCode { get; set; }
        public int PreviousSeniorityDays { get; set; }
        public string Status { get; set; }
        public DateTime CreateOn { get; set; }
    }

    public class LeaveRecord
    {
        public Guid RecordId { get; set; }
        public Guid EmployeeId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } // Pending, Completed, Cancelled
    }
}

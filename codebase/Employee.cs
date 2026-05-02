using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TubeHR.Foundation.Models
{
    [Table("PA_Employee", Schema = "fd")]
    public class Employee
    {
        [Key]
        public Guid EmployeeId { get; set; }
        public Guid CompanyId { get; set; }

        [Required, StringLength(20)]
        public string EmployeeNumber { get; set; }

        [StringLength(50)]
        public string ChineseName { get; set; }

        [StringLength(100)]
        public string EnglishName { get; set; }

        public Guid? DepartmentId { get; set; }

        [StringLength(50)]
        public string JobTitleCode { get; set; }

        /// <summary>
        /// 月薪。用於薪資同步和審批金額門檻計算。
        /// </summary>
        public double BaseSalary { get; set; }

        public DateTime HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }

        /// <summary>
        /// 狀態碼：A01(在職)、A13(留停)、A14(離職)
        /// </summary>
        [StringLength(3)]
        public string StatusCode { get; set; }

        /// <summary>
        /// 最低年資天數門檻，達標才能享有特定福利。
        /// 0 = 無門檻限制（不是 bug）。
        /// </summary>
        public int MinimumSeniorityDays { get; set; }

        public DateTime CreateOn { get; set; }
        public DateTime ModifyOn { get; set; }
        public Guid CreateBy { get; set; }
        public Guid ModifyBy { get; set; }
    }
}

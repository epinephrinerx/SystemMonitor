namespace SysMonitor.Tests;

/// <summary>
/// The break hints that stop a wrapped Thai label leaving one letter of a word
/// alone on the next line at a large font size (R-023). Pure string work, so
/// nothing here depends on which fonts the machine has.
/// </summary>
[TestClass]
public class ThaiWrapTests
{
    private const char Z = ThaiWrap.Break;

    [TestMethod]
    public void A_break_goes_in_front_of_each_leading_vowel_that_follows_a_thai_letter()
    {
        Assert.AreEqual($"แสดง{Z}ไดรฟ์{Z}เครือข่าย", ThaiWrap.Soften("แสดงไดรฟ์เครือข่าย"));
        Assert.AreEqual($"ตรวจหา{Z}เวอร์ชัน{Z}ใหม่", ThaiWrap.Soften("ตรวจหาเวอร์ชันใหม่"));
    }

    [TestMethod]
    public void The_first_letter_and_text_without_thai_are_left_alone()
    {
        Assert.AreEqual("แสดง CPU", ThaiWrap.Soften("แสดง CPU"));
        Assert.AreEqual("Show network drives", ThaiWrap.Soften("Show network drives"));
        Assert.AreEqual(string.Empty, ThaiWrap.Soften(string.Empty));
        Assert.AreEqual("เ", ThaiWrap.Soften("เ"));
    }

    [TestMethod]
    public void A_vowel_after_a_space_or_a_digit_needs_no_hint()
    {
        Assert.AreEqual("4.2.2 เวอร์ชัน", ThaiWrap.Soften("4.2.2 เวอร์ชัน"));
    }

    [TestMethod]
    public void Softening_twice_changes_nothing_and_removing_the_hints_restores_the_text()
    {
        const string text = "ตรวจหาเวอร์ชันใหม่ทุกครั้งที่เปิด";
        string once = ThaiWrap.Soften(text);
        Assert.AreEqual(once, ThaiWrap.Soften(once));
        Assert.AreEqual(text, once.Replace(Z.ToString(), string.Empty));
    }
}

namespace RentCollection.Infrastructure.Services;

public class KnetSettings
{
    public string TranportalId { get; set; } = string.Empty;
    public string TranportalPwd { get; set; } = string.Empty;
    public string TerminalResourceKey { get; set; } = string.Empty;
    public string PaymentApiUrl { get; set; } = "https://kpaytest.com.kw/kpg/PaymentHTTP.htm?param=paymentInit";
    public string PaymentApiUrlProd { get; set; } = "https://www.kpay.com.kw/kpg/PaymentHTTP.htm?param=paymentInit";
    public bool UseSandbox { get; set; } = true;
    public bool UseTestMode { get; set; } = false;
    public string SuccessUrl { get; set; } = string.Empty;
    public string ErrorUrl { get; set; } = string.Empty;
}

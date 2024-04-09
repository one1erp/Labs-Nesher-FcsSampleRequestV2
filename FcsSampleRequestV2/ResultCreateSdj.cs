namespace FcsSampleRequestV2
{
    public class ResultCreateSdj
    {
        public int sdgId { get; set; }
        public string message { get; set; }

        public ResultCreateSdj(int sdgId, string message)
        {
            this.sdgId = sdgId;
            this.message = message;
        }
    }
}

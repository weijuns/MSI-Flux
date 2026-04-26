using MessagePack;
using System;

namespace MSIFlux.IPC;

[MessagePackObject]
public class EcInfo
{
    [Key(0)]
    public string Version { get; set; }

    [Key(1)]
    public DateTime Date { get; set; }
}

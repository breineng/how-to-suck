using System;
namespace HowToSuck
{
 [Serializable]
 public sealed class LocalVideoSettings:IEquatable<LocalVideoSettings>
 {
  public int Width=1280,Height=720,Mode=3,Quality,Vsync=1;
  public uint RefreshNumerator=60,RefreshDenominator=1;
  // Mode values match Unity FullScreenMode: 0 exclusive, 1 borderless, 3 windowed; MaximizedWindow is not our Windows target.
  public bool Valid=>Width>=320&&Width<=16384&&Height>=200&&Height<=16384&&(Mode==0||Mode==1||Mode==3)&&Quality>=0&&Quality<64&&Vsync>=0&&Vsync<=2&&RefreshNumerator>0&&RefreshNumerator<=1000000&&RefreshDenominator>0&&RefreshDenominator<=1000000;
  public LocalVideoSettings Copy()=>(LocalVideoSettings)MemberwiseClone();
  public bool Equals(LocalVideoSettings x)=>x!=null&&Width==x.Width&&Height==x.Height&&Mode==x.Mode&&Quality==x.Quality&&Vsync==x.Vsync&&RefreshNumerator==x.RefreshNumerator&&RefreshDenominator==x.RefreshDenominator;
 }
 [Serializable]
 public sealed class LocalBindingOverride
 {
  public string Id,Path;
  public LocalBindingOverride Copy()=>new LocalBindingOverride{Id=Id,Path=Path};
 }
}

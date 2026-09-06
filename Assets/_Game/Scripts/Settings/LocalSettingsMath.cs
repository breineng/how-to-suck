using System;
namespace HowToSuck
{
    public static class LocalSettingsMath
    {
        // AudioMixer attenuation accepts dB, with an exact source mute gate for a zero linear setting.
        public static float Decibels(float linear)
        {if(float.IsNaN(linear)||float.IsInfinity(linear)||linear<0||linear>1)throw new ArgumentOutOfRangeException(nameof(linear));return linear<=.0001f?-80f:(float)(20*Math.Log10(linear));}
        public static float PitchDelta(float mouseY,float sensitivity,bool inverted)=>mouseY*sensitivity*(inverted?1:-1);
        public static string MixerParameter(int bus)
        {switch(bus){case 0:return "HTS_Master_dB";case 1:return "HTS_Vacuum_dB";case 2:return "HTS_Truck_dB";case 3:return "HTS_Impacts_dB";case 4:return "HTS_UI_dB";default:throw new ArgumentOutOfRangeException(nameof(bus));}}
    }
}

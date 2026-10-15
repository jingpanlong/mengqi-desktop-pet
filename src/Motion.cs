using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mengqi
{
    internal sealed partial class PetWindow
    {
        internal void VerifySmoothRendering()
        {
            for(int row=0;row<Durations.Length;row++) for(int stage=0;stage<8;stage++) using(Bitmap image=MotionPainter.Blend(Frames(row)[0],Frames(row)[1],stage/8.0)) Native.PaintLayer(Handle,image,Location,255);
        }
        private void PaintSmooth(long now,int frame)
        {
            int total=0;foreach(int duration in ActiveDurations[state]) total+=duration;
            double phase=Math.Max(0,now-stateStarted)*data.MotionSpeed/100.0%total;
            for(int i=0;i<frame;i++) phase-=ActiveDurations[state][i];
            double window=Math.Min(80,ActiveDurations[state][frame]*.55);
            double blend=Math.Max(0,(phase-(ActiveDurations[state][frame]-window))/window);
            // Smoothstep avoids a sudden velocity change at the transition ends.
            blend=blend*blend*(3-2*blend);int stage=(int)(blend*8);
            if(paintedState==state && paintedFrame==frame && paintedSmooth==stage) return;
            if(stage==0) PaintFrame(state,frame);
            else using(Bitmap image=MotionPainter.Blend(Frames(state)[frame],Frames(state)[(frame+1)%ActiveDurations[state].Length],stage/8.0)) Native.PaintLayer(Handle,image,Location,(byte)(data.Opacity*255/100));
            paintedState=state;paintedFrame=frame;paintedSmooth=stage;PositionBubble();
        }
    }
    internal static class MotionPainter
    {
        internal static Bitmap Blend(Bitmap a,Bitmap b,double blend)
        {
            if(a.Size!=b.Size) throw new ArgumentException("动画帧尺寸不同。");
            Bitmap result=new Bitmap(a.Width,a.Height,PixelFormat.Format32bppPArgb);Rectangle rect=new Rectangle(Point.Empty,a.Size);
            BitmapData x=a.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb),y=b.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb),z=result.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try {int length=x.Stride*a.Height;byte[] first=new byte[length],second=new byte[length];Marshal.Copy(x.Scan0,first,0,length);Marshal.Copy(y.Scan0,second,0,length);for(int i=0;i<length;i++) first[i]=(byte)Math.Round(first[i]*(1-blend)+second[i]*blend);Marshal.Copy(first,0,z.Scan0,length);}
            finally {a.UnlockBits(x);b.UnlockBits(y);result.UnlockBits(z);}return result;
        }
    }
}

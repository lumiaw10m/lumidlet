import zlib,struct,glob,sys,os
def read(p):
    d=open(p,'rb').read();i=8;w=h=0;idat=b'';ct=0
    while i<len(d):
        l=struct.unpack('>I',d[i:i+4])[0];t=d[i+4:i+8];c=d[i+8:i+8+l];i+=12+l
        if t==b'IHDR': w,h,bd,ct=struct.unpack('>IIBB',c[:10])
        elif t==b'IDAT': idat+=c
    raw=zlib.decompress(idat);bpp=4 if ct==6 else 3;st=w*bpp;out=[];prev=bytearray(st);p=0
    for y in range(h):
        f=raw[p];p+=1;cur=bytearray(raw[p:p+st]);p+=st
        for x in range(st):
            a=cur[x-bpp] if x>=bpp else 0;b=prev[x];c=prev[x-bpp] if x>=bpp else 0
            if f==1:cur[x]=(cur[x]+a)&255
            elif f==2:cur[x]=(cur[x]+b)&255
            elif f==3:cur[x]=(cur[x]+(a+b)//2)&255
            elif f==4:
                pp=a+b-c;pa,pb,pc=abs(pp-a),abs(pp-b),abs(pp-c)
                cur[x]=(cur[x]+(a if pa<=pb and pa<=pc else b if pb<=pc else c))&255
        out.append(bytes(cur));prev=cur
    return w,h,bpp,out
d=sys.argv[1]
files=sorted(glob.glob(os.path.join(d,'t*.png')))
imgs=[read(f) for f in files]
W=sum(i[0] for i in imgs)+4*len(imgs);H=max(i[1] for i in imgs)
rows=[]
for y in range(H):
    r=bytearray()
    for w,h,bpp,o in imgs:
        line=o[y] if y<h else bytes(w*bpp)
        for x in range(w): r+=line[x*bpp:x*bpp+3]
        r+=b'\x80\x80\x80'*4
    rows.append(b'\x00'+bytes(r))
def ch(t,dd): return struct.pack('>I',len(dd))+t+dd+struct.pack('>I',zlib.crc32(t+dd)&0xffffffff)
open(os.path.join(d,'montage.png'),'wb').write(b'\x89PNG\r\n\x1a\n'+ch(b'IHDR',struct.pack('>IIBBBBB',W,H,8,2,0,0,0))+ch(b'IDAT',zlib.compress(b''.join(rows)))+ch(b'IEND',b''))

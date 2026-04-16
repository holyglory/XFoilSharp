/* Xwin_stub.c -- headless replacement for Xwin.c.
 *
 * Provides no-op implementations of every gwx* symbol consumed by the
 * Fortran plot library. Linking this file in place of Xwin.c yields a
 * truly headless XFoil build that does not depend on libX11 or open any
 * windows. All plot operations become silent no-ops, while numerical
 * results are unaffected.
 *
 * The function signatures must match Xwin.c exactly so the Fortran ABI
 * (with the trailing underscore added by gfortran) lines up.
 */

#include <stdio.h>

/* Symbol decoration matches Xwin.c built with -DUNDERSCORE. */
#ifndef UNDERSCORE
#define UNDERSCORE
#endif

#ifdef UNDERSCORE
#define GWXREVFLAG       gwxrevflag_
#define GWXOPEN          gwxopen_
#define GWXWINOPEN       gwxwinopen_
#define GWXCLEAR         gwxclear_
#define GWXSTATUS        gwxstatus_
#define GWXRESIZE        gwxresize_
#define GWXRESET         gwxreset_
#define GWXCLOSE         gwxclose_
#define GWXFLUSH         gwxflush_
#define GWXLINE          gwxline_
#define GWXDASH          gwxdash_
#define GWXCURS          gwxcurs_
#define GWXPEN           gwxpen_
#define GWXDESTROY       gwxdestroy_
#define GWXLINEZ         gwxlinez_
#define GWXPOLY          gwxpoly_
#define GWXSTRING        gwxstring_
#define GWXSETCOLOR      gwxsetcolor_
#define GWXSETBGCOLOR    gwxsetbgcolor_
#define GWXCOLORNAME2RGB gwxcolorname2rgb_
#define GWXALLOCRGBCOLOR gwxallocrgbcolor_
#define GWXFREECOLOR     gwxfreecolor_
#define GWXDISPLAYBUFFER gwxdisplaybuffer_
#define GWXDRAWTOBUFFER  gwxdrawtobuffer_
#define GWXDRAWTOWINDOW  gwxdrawtowindow_
#else
#define GWXREVFLAG       gwxrevflag
#define GWXOPEN          gwxopen
#define GWXWINOPEN       gwxwinopen
#define GWXCLEAR         gwxclear
#define GWXSTATUS        gwxstatus
#define GWXRESIZE        gwxresize
#define GWXRESET         gwxreset
#define GWXCLOSE         gwxclose
#define GWXFLUSH         gwxflush
#define GWXLINE          gwxline
#define GWXDASH          gwxdash
#define GWXCURS          gwxcurs
#define GWXPEN           gwxpen
#define GWXDESTROY       gwxdestroy
#define GWXLINEZ         gwxlinez
#define GWXPOLY          gwxpoly
#define GWXSTRING        gwxstring
#define GWXSETCOLOR      gwxsetcolor
#define GWXSETBGCOLOR    gwxsetbgcolor
#define GWXCOLORNAME2RGB gwxcolorname2rgb
#define GWXALLOCRGBCOLOR gwxallocrgbcolor
#define GWXFREECOLOR     gwxfreecolor
#define GWXDISPLAYBUFFER gwxdisplaybuffer
#define GWXDRAWTOBUFFER  gwxdrawtobuffer
#define GWXDRAWTOWINDOW  gwxdrawtowindow
#endif

void GWXREVFLAG(int *revflag) { (void)revflag; }

void GWXOPEN(int *xsizeroot, int *ysizeroot, int *depth)
{
    *xsizeroot = 800;
    *ysizeroot = 600;
    *depth = 24;
}

void GWXWINOPEN(int *xstart, int *ystart, int *xsize, int *ysize)
{
    (void)xstart; (void)ystart; (void)xsize; (void)ysize;
}

void GWXCLEAR(void) {}
void GWXSTATUS(int *xstart, int *ystart, int *xsize, int *ysize)
{
    (void)xstart; (void)ystart; (void)xsize; (void)ysize;
}
void GWXRESIZE(int *x, int *y) { (void)x; (void)y; }
void GWXRESET(void) {}
void GWXCLOSE(void) {}
void GWXDESTROY(void) {}
void GWXFLUSH(void) {}
void GWXLINE(float *x1, float *y1, float *x2, float *y2)
{ (void)x1; (void)y1; (void)x2; (void)y2; }
void GWXLINEZ(int *ix, int *iy, int *n) { (void)ix; (void)iy; (void)n; }
void GWXPOLY(float *x, float *y, int *n) { (void)x; (void)y; (void)n; }
void GWXSTRING(float *x, float *y, char *str, int *len) { (void)x; (void)y; (void)str; (void)len; }
void GWXSETCOLOR(int *pixel) { (void)pixel; }
void GWXSETBGCOLOR(int *pixel) { (void)pixel; }
void GWXCOLORNAME2RGB(char *name, int *namelen, int *r, int *g, int *b)
{ (void)name; (void)namelen; *r = 0; *g = 0; *b = 0; }
void GWXALLOCRGBCOLOR(int *r, int *g, int *b, int *ic)
{ (void)r; (void)g; (void)b; *ic = 0; }
void GWXFREECOLOR(int *pix) { (void)pix; }
void GWXDISPLAYBUFFER(void) {}
void GWXDRAWTOBUFFER(void) {}
void GWXDRAWTOWINDOW(void) {}
void GWXDASH(int *lmask) { (void)lmask; }
void GWXCURS(int *x, int *y, int *state)
{ (void)x; (void)y; *state = 0; }
void GWXPEN(int *ipen) { (void)ipen; }

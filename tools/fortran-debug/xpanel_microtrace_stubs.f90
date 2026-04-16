subroutine trace_enter(scope)
  implicit none
  character(len=*), intent(in) :: scope
end subroutine trace_enter

subroutine trace_exit(scope)
  implicit none
  character(len=*), intent(in) :: scope
end subroutine trace_exit

subroutine trace_psilin_field(scope, fieldindex, xval, yval, nxval, nyval, geolin, siglin)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  real, intent(in) :: xval
  real, intent(in) :: yval
  real, intent(in) :: nxval
  real, intent(in) :: nyval
  logical, intent(in) :: geolin
  logical, intent(in) :: siglin
end subroutine trace_psilin_field

subroutine trace_psilin_panel(scope, fieldindex, panelindex, jm, jo, jp, jq, geolin, siglin, &
    panelxjo, panelyjo, panelxjp, panelyjp, paneldx, paneldy, dso, dsio, apan, &
    rx1, ry1, rx2, ry2, sx, sy, x1, x2, yy, rs1, rs2, sgn, g1, g2, t1, t2, x1i, x2i, yyi)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  integer, intent(in) :: panelindex
  integer, intent(in) :: jm
  integer, intent(in) :: jo
  integer, intent(in) :: jp
  integer, intent(in) :: jq
  logical, intent(in) :: geolin
  logical, intent(in) :: siglin
  real, intent(in) :: panelxjo
  real, intent(in) :: panelyjo
  real, intent(in) :: panelxjp
  real, intent(in) :: panelyjp
  real, intent(in) :: paneldx
  real, intent(in) :: paneldy
  real, intent(in) :: dso
  real, intent(in) :: dsio
  real, intent(in) :: apan
  real, intent(in) :: rx1
  real, intent(in) :: ry1
  real, intent(in) :: rx2
  real, intent(in) :: ry2
  real, intent(in) :: sx
  real, intent(in) :: sy
  real, intent(in) :: x1
  real, intent(in) :: x2
  real, intent(in) :: yy
  real, intent(in) :: rs1
  real, intent(in) :: rs2
  real, intent(in) :: sgn
  real, intent(in) :: g1
  real, intent(in) :: g2
  real, intent(in) :: t1
  real, intent(in) :: t2
  real, intent(in) :: x1i
  real, intent(in) :: x2i
  real, intent(in) :: yyi
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,28(1X,Z8.8))') &
    'PANEL', current_case, panelindex, &
    transfer(panelxjo, 0), transfer(panelyjo, 0), transfer(panelxjp, 0), transfer(panelyjp, 0), &
    transfer(paneldx, 0), transfer(paneldy, 0), transfer(dso, 0), transfer(dsio, 0), transfer(apan, 0), &
    transfer(rx1, 0), transfer(ry1, 0), transfer(rx2, 0), transfer(ry2, 0), &
    transfer(sx, 0), transfer(sy, 0), transfer(x1, 0), transfer(x2, 0), transfer(yy, 0), &
    transfer(rs1, 0), transfer(rs2, 0), transfer(sgn, 0), transfer(g1, 0), transfer(g2, 0), &
    transfer(t1, 0), transfer(t2, 0), transfer(x1i, 0), transfer(x2i, 0), transfer(yyi, 0)
end subroutine trace_psilin_panel

subroutine trace_psilin_source_half_terms(scope, fieldindex, panelindex, half, x0, &
    psumterm1, psumterm2, psumterm3, psumaccum, psum, &
    pdifterm1, pdifterm2, pdifterm3, pdifterm4, &
    pdifaccum1, pdifaccum2, pdifnumerator, pdif)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  integer, intent(in) :: panelindex
  integer, intent(in) :: half
  real, intent(in) :: x0
  real, intent(in) :: psumterm1
  real, intent(in) :: psumterm2
  real, intent(in) :: psumterm3
  real, intent(in) :: psumaccum
  real, intent(in) :: psum
  real, intent(in) :: pdifterm1
  real, intent(in) :: pdifterm2
  real, intent(in) :: pdifterm3
  real, intent(in) :: pdifterm4
  real, intent(in) :: pdifaccum1
  real, intent(in) :: pdifaccum2
  real, intent(in) :: pdifnumerator
  real, intent(in) :: pdif
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,1X,I0,14(1X,Z8.8))') &
    'HALF', current_case, panelindex, half, &
    transfer(x0, 0), transfer(psumterm1, 0), transfer(psumterm2, 0), transfer(psumterm3, 0), &
    transfer(psumaccum, 0), transfer(psum, 0), &
    transfer(pdifterm1, 0), transfer(pdifterm2, 0), transfer(pdifterm3, 0), transfer(pdifterm4, 0), &
    transfer(pdifaccum1, 0), transfer(pdifaccum2, 0), transfer(pdifnumerator, 0), transfer(pdif, 0)
end subroutine trace_psilin_source_half_terms

subroutine trace_psilin_source_dz_terms(scope, fieldindex, panelindex, half, &
    dzjmterm1, dzjmterm2, dzjminner, dzjoterm1, dzjoterm2, dzjoinner, &
    dzjpterm1, dzjpterm2, dzjpinner, dzjqterm1, dzjqterm2, dzjqinner)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  integer, intent(in) :: panelindex
  integer, intent(in) :: half
  real, intent(in) :: dzjmterm1
  real, intent(in) :: dzjmterm2
  real, intent(in) :: dzjminner
  real, intent(in) :: dzjoterm1
  real, intent(in) :: dzjoterm2
  real, intent(in) :: dzjoinner
  real, intent(in) :: dzjpterm1
  real, intent(in) :: dzjpterm2
  real, intent(in) :: dzjpinner
  real, intent(in) :: dzjqterm1
  real, intent(in) :: dzjqterm2
  real, intent(in) :: dzjqinner
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,1X,I0,12(1X,Z8.8))') &
    'DZ', current_case, panelindex, half, &
    transfer(dzjmterm1, 0), transfer(dzjmterm2, 0), transfer(dzjminner, 0), &
    transfer(dzjoterm1, 0), transfer(dzjoterm2, 0), transfer(dzjoinner, 0), &
    transfer(dzjpterm1, 0), transfer(dzjpterm2, 0), transfer(dzjpinner, 0), &
    transfer(dzjqterm1, 0), transfer(dzjqterm2, 0), transfer(dzjqinner, 0)
end subroutine trace_psilin_source_dz_terms

subroutine trace_psilin_source_dq_terms(scope, fieldindex, panelindex, half, &
    dqjmterm1, dqjmterm2, dqjminner, dqjoterm1, dqjoterm2, dqjoinner, &
    dqjpterm1, dqjpterm2, dqjpinner, dqjqterm1, dqjqterm2, dqjqinner)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  integer, intent(in) :: panelindex
  integer, intent(in) :: half
  real, intent(in) :: dqjmterm1
  real, intent(in) :: dqjmterm2
  real, intent(in) :: dqjminner
  real, intent(in) :: dqjoterm1
  real, intent(in) :: dqjoterm2
  real, intent(in) :: dqjoinner
  real, intent(in) :: dqjpterm1
  real, intent(in) :: dqjpterm2
  real, intent(in) :: dqjpinner
  real, intent(in) :: dqjqterm1
  real, intent(in) :: dqjqterm2
  real, intent(in) :: dqjqinner
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,1X,I0,12(1X,Z8.8))') &
    'DQ', current_case, panelindex, half, &
    transfer(dqjmterm1, 0), transfer(dqjmterm2, 0), transfer(dqjminner, 0), &
    transfer(dqjoterm1, 0), transfer(dqjoterm2, 0), transfer(dqjoinner, 0), &
    transfer(dqjpterm1, 0), transfer(dqjpterm2, 0), transfer(dqjpinner, 0), &
    transfer(dqjqterm1, 0), transfer(dqjqterm2, 0), transfer(dqjqinner, 0)
end subroutine trace_psilin_source_dq_terms

subroutine trace_psilin_source_segment(scope, fieldindex, panelindex, half, jm, jo, jp, jq, &
    x1, x2, yy, apan, x1i, x2i, yyi, rs0, rs1, rs2, g0, g1, g2, t0, t1, t2, &
    dso, dsio, dsm, dsim, dsp, dsip, dxinv, sourcetermleft, sourcetermright, ssum, sdif, psum, pdif, &
    psx0, psx1, psx2, psyy, &
    pdx0term1, pdx0term2, pdx0numerator, pdx0, &
    pdx1term1, pdx1term2, pdx1numerator, pdx1, &
    pdx2term1, pdx2term2, pdx2numerator, pdx2, &
    pdyyterm1, pdyyterm2, pdyynumerator, pdyy, psniterm1, psniterm2, psniterm3, psni, &
    pdniterm1, pdniterm2, pdniterm3, pdni, &
    dzjm, dzjo, dzjp, dzjq, dqjm, dqjo, dqjp, dqjq)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  integer, intent(in) :: panelindex
  integer, intent(in) :: half
  integer, intent(in) :: jm
  integer, intent(in) :: jo
  integer, intent(in) :: jp
  integer, intent(in) :: jq
  real, intent(in) :: x1
  real, intent(in) :: x2
  real, intent(in) :: yy
  real, intent(in) :: apan
  real, intent(in) :: x1i
  real, intent(in) :: x2i
  real, intent(in) :: yyi
  real, intent(in) :: rs0
  real, intent(in) :: rs1
  real, intent(in) :: rs2
  real, intent(in) :: g0
  real, intent(in) :: g1
  real, intent(in) :: g2
  real, intent(in) :: t0
  real, intent(in) :: t1
  real, intent(in) :: t2
  real, intent(in) :: dso
  real, intent(in) :: dsio
  real, intent(in) :: dsm
  real, intent(in) :: dsim
  real, intent(in) :: dsp
  real, intent(in) :: dsip
  real, intent(in) :: dxinv
  real, intent(in) :: sourcetermleft
  real, intent(in) :: sourcetermright
  real, intent(in) :: ssum
  real, intent(in) :: sdif
  real, intent(in) :: psum
  real, intent(in) :: pdif
  real, intent(in) :: psx0
  real, intent(in) :: psx1
  real, intent(in) :: psx2
  real, intent(in) :: psyy
  real, intent(in) :: pdx0term1
  real, intent(in) :: pdx0term2
  real, intent(in) :: pdx0numerator
  real, intent(in) :: pdx0
  real, intent(in) :: pdx1term1
  real, intent(in) :: pdx1term2
  real, intent(in) :: pdx1numerator
  real, intent(in) :: pdx1
  real, intent(in) :: pdx2term1
  real, intent(in) :: pdx2term2
  real, intent(in) :: pdx2numerator
  real, intent(in) :: pdx2
  real, intent(in) :: pdyyterm1
  real, intent(in) :: pdyyterm2
  real, intent(in) :: pdyynumerator
  real, intent(in) :: pdyy
  real, intent(in) :: psniterm1
  real, intent(in) :: psniterm2
  real, intent(in) :: psniterm3
  real, intent(in) :: psni
  real, intent(in) :: pdniterm1
  real, intent(in) :: pdniterm2
  real, intent(in) :: pdniterm3
  real, intent(in) :: pdni
  real, intent(in) :: dzjm
  real, intent(in) :: dzjo
  real, intent(in) :: dzjp
  real, intent(in) :: dzjq
  real, intent(in) :: dqjm
  real, intent(in) :: dqjo
  real, intent(in) :: dqjp
  real, intent(in) :: dqjq
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,1X,I0,65(1X,Z8.8))') &
    'SEG', current_case, panelindex, half, &
    transfer(x1, 0), transfer(x2, 0), transfer(yy, 0), transfer(apan, 0), &
    transfer(x1i, 0), transfer(x2i, 0), transfer(yyi, 0), &
    transfer(rs0, 0), transfer(rs1, 0), transfer(rs2, 0), &
    transfer(g0, 0), transfer(g1, 0), transfer(g2, 0), &
    transfer(t0, 0), transfer(t1, 0), transfer(t2, 0), &
    transfer(dso, 0), transfer(dsio, 0), transfer(dsm, 0), transfer(dsim, 0), &
    transfer(dsp, 0), transfer(dsip, 0), transfer(dxinv, 0), &
    transfer(sourcetermleft, 0), transfer(sourcetermright, 0), &
    transfer(ssum, 0), transfer(sdif, 0), transfer(psum, 0), transfer(pdif, 0), &
    transfer(psx0, 0), transfer(psx1, 0), transfer(psx2, 0), transfer(psyy, 0), &
    transfer(pdx0term1, 0), transfer(pdx0term2, 0), transfer(pdx0numerator, 0), transfer(pdx0, 0), &
    transfer(pdx1term1, 0), transfer(pdx1term2, 0), transfer(pdx1numerator, 0), transfer(pdx1, 0), &
    transfer(pdx2term1, 0), transfer(pdx2term2, 0), transfer(pdx2numerator, 0), transfer(pdx2, 0), &
    transfer(pdyyterm1, 0), transfer(pdyyterm2, 0), transfer(pdyynumerator, 0), transfer(pdyy, 0), &
    transfer(psniterm1, 0), transfer(psniterm2, 0), transfer(psniterm3, 0), transfer(psni, 0), &
    transfer(pdniterm1, 0), transfer(pdniterm2, 0), transfer(pdniterm3, 0), transfer(pdni, 0), &
    transfer(dzjm, 0), transfer(dzjo, 0), transfer(dzjp, 0), transfer(dzjq, 0), &
    transfer(dqjm, 0), transfer(dqjo, 0), transfer(dqjp, 0), transfer(dqjq, 0)
end subroutine trace_psilin_source_segment

subroutine trace_psilin_accum_state(scope, fieldindex, stage, jo, jp, psibefore, psinibefore, psival, psinival)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  character(len=*), intent(in) :: stage
  integer, intent(in) :: jo
  integer, intent(in) :: jp
  real, intent(in) :: psibefore
  real, intent(in) :: psinibefore
  real, intent(in) :: psival
  real, intent(in) :: psinival
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,A,1X,I0,1X,I0,4(1X,Z8.8))') &
    'ACCUM', current_case, trim(stage), jo, jp, &
    transfer(psibefore, 0), transfer(psinibefore, 0), transfer(psival, 0), transfer(psinival, 0)
end subroutine trace_psilin_accum_state

subroutine trace_psilin_result_terms(scope, fieldindex, psibefore, psinibefore, psifreestreamdelta, psinifreestreamdelta)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  real, intent(in) :: psibefore
  real, intent(in) :: psinibefore
  real, intent(in) :: psifreestreamdelta
  real, intent(in) :: psinifreestreamdelta
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,4(1X,Z8.8))') &
    'RTERM', current_case, fieldindex, &
    transfer(psibefore, 0), transfer(psinibefore, 0), &
    transfer(psifreestreamdelta, 0), transfer(psinifreestreamdelta, 0)
end subroutine trace_psilin_result_terms

subroutine trace_psilin_result(scope, fieldindex, psival, psinival)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  real, intent(in) :: psival
  real, intent(in) :: psinival
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,2(1X,Z8.8))') &
    'RESULT', current_case, fieldindex, transfer(psival, 0), transfer(psinival, 0)
end subroutine trace_psilin_result

subroutine trace_psilin_vortex_segment(scope, fieldindex, paneljo, paneljp, &
    x1, x2, yy, rs1, rs2, g1, g2, t1, t2, dxinv, &
    psist1, psist2, psist3, psist4, psis, psidt1, psidt2, psidt3, psidt4, psidt5, psidh, psid, &
    psx1, psx2, psyy, pdxsum, pdx1mul, pdx1pan, pdx1a1, pdx1a2, pdx1num, &
    pdx1, pdx2mul, pdx2pan, pdx2a1, pdx2a2, pdx2num, pdx2, pdyy, &
    gammajo, gammajp, gsum, gdif, psni, pdni, psidlt, psnidlt, dzjo, dzjp, dqjo, dqjp)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  integer, intent(in) :: paneljo
  integer, intent(in) :: paneljp
  real, intent(in) :: x1
  real, intent(in) :: x2
  real, intent(in) :: yy
  real, intent(in) :: rs1
  real, intent(in) :: rs2
  real, intent(in) :: g1
  real, intent(in) :: g2
  real, intent(in) :: t1
  real, intent(in) :: t2
  real, intent(in) :: dxinv
  real, intent(in) :: psist1
  real, intent(in) :: psist2
  real, intent(in) :: psist3
  real, intent(in) :: psist4
  real, intent(in) :: psis
  real, intent(in) :: psidt1
  real, intent(in) :: psidt2
  real, intent(in) :: psidt3
  real, intent(in) :: psidt4
  real, intent(in) :: psidt5
  real, intent(in) :: psidh
  real, intent(in) :: psid
  real, intent(in) :: psx1
  real, intent(in) :: psx2
  real, intent(in) :: psyy
  real, intent(in) :: pdxsum
  real, intent(in) :: pdx1mul
  real, intent(in) :: pdx1pan
  real, intent(in) :: pdx1a1
  real, intent(in) :: pdx1a2
  real, intent(in) :: pdx1num
  real, intent(in) :: pdx1
  real, intent(in) :: pdx2mul
  real, intent(in) :: pdx2pan
  real, intent(in) :: pdx2a1
  real, intent(in) :: pdx2a2
  real, intent(in) :: pdx2num
  real, intent(in) :: pdx2
  real, intent(in) :: pdyy
  real, intent(in) :: gammajo
  real, intent(in) :: gammajp
  real, intent(in) :: gsum
  real, intent(in) :: gdif
  real, intent(in) :: psni
  real, intent(in) :: pdni
  real, intent(in) :: psidlt
  real, intent(in) :: psnidlt
  real, intent(in) :: dzjo
  real, intent(in) :: dzjp
  real, intent(in) :: dqjo
  real, intent(in) :: dqjp
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,1X,I0,51(1X,Z8.8))') &
    'VOR', current_case, paneljo, paneljp, &
    transfer(x1, 0), transfer(x2, 0), transfer(yy, 0), &
    transfer(rs1, 0), transfer(rs2, 0), transfer(g1, 0), transfer(g2, 0), &
    transfer(t1, 0), transfer(t2, 0), transfer(dxinv, 0), &
    transfer(psist1, 0), transfer(psist2, 0), transfer(psist3, 0), transfer(psist4, 0), transfer(psis, 0), &
    transfer(psidt1, 0), transfer(psidt2, 0), transfer(psidt3, 0), transfer(psidt4, 0), transfer(psidt5, 0), &
    transfer(psidh, 0), transfer(psid, 0), &
    transfer(psx1, 0), transfer(psx2, 0), transfer(psyy, 0), &
    transfer(pdxsum, 0), transfer(pdx1mul, 0), transfer(pdx1pan, 0), transfer(pdx1a1, 0), transfer(pdx1a2, 0), &
    transfer(pdx1num, 0), transfer(pdx1, 0), &
    transfer(pdx2mul, 0), transfer(pdx2pan, 0), transfer(pdx2a1, 0), transfer(pdx2a2, 0), transfer(pdx2num, 0), &
    transfer(pdx2, 0), transfer(pdyy, 0), &
    transfer(gammajo, 0), transfer(gammajp, 0), transfer(gsum, 0), transfer(gdif, 0), transfer(psni, 0), transfer(pdni, 0), &
    transfer(psidlt, 0), transfer(psnidlt, 0), &
    transfer(dzjo, 0), transfer(dzjp, 0), transfer(dqjo, 0), transfer(dqjp, 0)
end subroutine trace_psilin_vortex_segment

subroutine trace_psilin_te_correction(scope, fieldindex, jo, jp, psig, pgam, psigni, pgamni, sigte, gamte, &
    scs, sds, dzjotesig, dzjptesig, dzjotegam, dzjptegam, dqjotesighalf, dqjotesigterm, &
    dqjotegamhalf, dqjotegamterm, dqteinner, dqjote, dqjpte)
  implicit none
  character(len=*), intent(in) :: scope
  integer, intent(in) :: fieldindex
  integer, intent(in) :: jo
  integer, intent(in) :: jp
  real, intent(in) :: psig
  real, intent(in) :: pgam
  real, intent(in) :: psigni
  real, intent(in) :: pgamni
  real, intent(in) :: sigte
  real, intent(in) :: gamte
  real, intent(in) :: scs
  real, intent(in) :: sds
  real, intent(in) :: dzjotesig
  real, intent(in) :: dzjptesig
  real, intent(in) :: dzjotegam
  real, intent(in) :: dzjptegam
  real, intent(in) :: dqjotesighalf
  real, intent(in) :: dqjotesigterm
  real, intent(in) :: dqjotegamhalf
  real, intent(in) :: dqjotegamterm
  real, intent(in) :: dqteinner
  real, intent(in) :: dqjote
  real, intent(in) :: dqjpte
  integer :: current_case

  common /psilin_driver_state/ current_case

  write(*, '(A,1X,I0,1X,I0,1X,I0,19(1X,Z8.8))') &
    'TE', current_case, jo, jp, &
    transfer(psig, 0), transfer(pgam, 0), transfer(psigni, 0), transfer(pgamni, 0), &
    transfer(sigte, 0), transfer(gamte, 0), transfer(scs, 0), transfer(sds, 0), &
    transfer(dzjotesig, 0), transfer(dzjptesig, 0), transfer(dzjotegam, 0), transfer(dzjptegam, 0), &
    transfer(dqjotesighalf, 0), transfer(dqjotesigterm, 0), transfer(dqjotegamhalf, 0), transfer(dqjotegamterm, 0), &
    transfer(dqteinner, 0), transfer(dqjote, 0), transfer(dqjpte, 0)
end subroutine trace_psilin_te_correction

subroutine trace_basis_entry()
end subroutine trace_basis_entry

subroutine trace_column_entry()
end subroutine trace_column_entry

subroutine trace_matrix_entry()
end subroutine trace_matrix_entry

subroutine trace_panel_node()
end subroutine trace_panel_node

subroutine trace_pivot_entry()
end subroutine trace_pivot_entry

subroutine trace_predicted_edge_velocity()
end subroutine trace_predicted_edge_velocity

subroutine trace_predicted_edge_velocity_term()
end subroutine trace_predicted_edge_velocity_term

subroutine trace_pswlin_field()
end subroutine trace_pswlin_field

subroutine trace_pswlin_geometry()
end subroutine trace_pswlin_geometry

subroutine trace_pswlin_half_terms()
end subroutine trace_pswlin_half_terms

subroutine trace_pswlin_pdx0_terms()
end subroutine trace_pswlin_pdx0_terms

subroutine trace_pswlin_pdx1_terms()
end subroutine trace_pswlin_pdx1_terms

subroutine trace_pswlin_pdx2_terms()
end subroutine trace_pswlin_pdx2_terms

subroutine trace_pswlin_ni_terms()
end subroutine trace_pswlin_ni_terms

subroutine trace_pswlin_recurrence()
end subroutine trace_pswlin_recurrence

subroutine trace_pswlin_segment()
end subroutine trace_pswlin_segment

subroutine trace_realhex()
end subroutine trace_realhex

subroutine trace_stagnation_candidate()
end subroutine trace_stagnation_candidate

subroutine trace_stagnation_interpolation()
end subroutine trace_stagnation_interpolation

subroutine trace_stagnation_speed_window()
end subroutine trace_stagnation_speed_window

subroutine trace_text()
end subroutine trace_text

subroutine trace_wake_panel_state()
end subroutine trace_wake_panel_state

subroutine trace_wake_source_accum()
end subroutine trace_wake_source_accum

subroutine trace_wake_source_entry()
end subroutine trace_wake_source_entry

subroutine trace_wake_spacing()
end subroutine trace_wake_spacing

subroutine trace_wake_spacing_input()
end subroutine trace_wake_spacing_input

subroutine trace_wake_step_terms()
end subroutine trace_wake_step_terms

subroutine segspl()
end subroutine segspl

subroutine setexp()
end subroutine setexp

subroutine ludcmp()
end subroutine ludcmp

subroutine baksub()
end subroutine baksub

subroutine iblsys()
end subroutine iblsys

real function atanc()
  implicit none
  atanc = 0.0
end function atanc

subroutine trace_psilin_source_pdyy_write(a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p,q,r,s,t,u,v)
  character(*) a; integer b,c,d; character(*) e
  real f,g,h,i,j,k,l,m,n,o,p,q,r,s,t,u,v
end subroutine

subroutine trace_psilin_te_pgam_terms(a,b,c,d,e,f,g,h,i,j,k,l)
  character(*) a; integer b; real c,d,e,f,g,h,i,j,k,l
end subroutine

! FMA wrapper: respects XFOIL_DISABLE_FMA env var.
real function fmaf_real(a, b, c) result(r)
  use iso_c_binding, only: c_float
  implicit none
  real, intent(in) :: a, b, c
  interface
    real(c_float) function c_fmaf(x, y, z) bind(C, name='fmaf')
      import :: c_float
      real(c_float), value :: x, y, z
    end function c_fmaf
  end interface
  logical, save :: checked = .false.
  logical, save :: disable_fma = .false.
  character(len=8) :: env_val
  integer :: env_len, env_stat
  if (.not. checked) then
    call get_environment_variable('XFOIL_DISABLE_FMA', env_val, env_len, env_stat)
    disable_fma = (env_stat == 0 .and. env_len > 0)
    checked = .true.
  end if
  if (disable_fma) then
    r = a * b + c
  else
    r = c_fmaf(a, b, c)
  end if
end function fmaf_real

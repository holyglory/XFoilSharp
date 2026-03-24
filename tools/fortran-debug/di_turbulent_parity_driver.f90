program di_turbulent_parity_driver
  use bl_common_kernels, only: cft_kernel
  implicit none

  integer :: case_count
  integer :: case_index
  real :: hk
  real :: hs
  real :: us
  real :: rt
  real :: s
  real :: msq
  real :: hk_t
  real :: hk_d
  real :: hk_u
  real :: hk_ms
  real :: hs_t
  real :: hs_d
  real :: hs_u
  real :: hs_ms
  real :: us_t
  real :: us_d
  real :: us_u
  real :: us_ms
  real :: rt_t
  real :: rt_u
  real :: rt_ms
  real :: m_u
  real :: m_ms
  real :: di
  real :: di_s
  real :: di_t
  real :: di_d
  real :: di_u
  real :: di_ms
  real :: wall_di
  real :: wall_s
  real :: wall_t
  real :: wall_d
  real :: wall_u
  real :: wall_ms
  real :: dfac_di
  real :: dfac_s
  real :: dfac_t
  real :: dfac_d
  real :: dfac_u
  real :: dfac_ms
  real :: dd_stage
  real :: dd_s_stage
  real :: dd_t
  real :: dd_d
  real :: dd_u
  real :: dd_ms
  real :: postdd_di
  real :: postdd_s
  real :: postdd_t
  real :: postdd_d
  real :: postdd_u
  real :: postdd_ms
  real :: ddl_stage
  real :: ddl_t
  real :: ddl_d
  real :: ddl_u
  real :: ddl_ms
  real :: postddl_di
  real :: postddl_s
  real :: postddl_t
  real :: postddl_d
  real :: postddl_u
  real :: postddl_ms
  real :: dil_stage
  real :: dil_t
  real :: dil_d
  real :: dil_u
  real :: dil_ms

  read(*,*) case_count
  write(*,'(I8)') case_count

  do case_index = 1, case_count
    read(*,*) hk, hs, us, rt, s, msq
    read(*,*) hk_t, hk_d, hk_u, hk_ms
    read(*,*) hs_t, hs_d, hs_u, hs_ms
    read(*,*) us_t, us_d, us_u, us_ms
    read(*,*) rt_t, rt_u, rt_ms
    read(*,*) m_u, m_ms

    call compute_turbulent_di_chain(hk, hs, us, rt, s, msq, &
      hk_t, hk_d, hk_u, hk_ms, hs_t, hs_d, hs_u, hs_ms, &
      us_t, us_d, us_u, us_ms, rt_t, rt_u, rt_ms, m_u, m_ms, &
      di, di_s, di_t, di_d, di_u, di_ms)

    write(*,'(A,1X,I0,6(1X,Z8.8))') 'WALL', case_index, &
      transfer(wall_di, 0), transfer(wall_s, 0), transfer(wall_t, 0), &
      transfer(wall_d, 0), transfer(wall_u, 0), transfer(wall_ms, 0)
    write(*,'(A,1X,I0,6(1X,Z8.8))') 'DFAC', case_index, &
      transfer(dfac_di, 0), transfer(dfac_s, 0), transfer(dfac_t, 0), &
      transfer(dfac_d, 0), transfer(dfac_u, 0), transfer(dfac_ms, 0)
    write(*,'(A,1X,I0,6(1X,Z8.8))') 'DD', case_index, &
      transfer(dd_stage, 0), transfer(dd_s_stage, 0), transfer(dd_t, 0), &
      transfer(dd_d, 0), transfer(dd_u, 0), transfer(dd_ms, 0)
    write(*,'(A,1X,I0,6(1X,Z8.8))') 'POSTDD', case_index, &
      transfer(postdd_di, 0), transfer(postdd_s, 0), transfer(postdd_t, 0), &
      transfer(postdd_d, 0), transfer(postdd_u, 0), transfer(postdd_ms, 0)
    write(*,'(A,1X,I0,6(1X,Z8.8))') 'DDL', case_index, &
      transfer(ddl_stage, 0), transfer(0.0, 0), transfer(ddl_t, 0), &
      transfer(ddl_d, 0), transfer(ddl_u, 0), transfer(ddl_ms, 0)
    write(*,'(A,1X,I0,6(1X,Z8.8))') 'POSTDDL', case_index, &
      transfer(postddl_di, 0), transfer(postddl_s, 0), transfer(postddl_t, 0), &
      transfer(postddl_d, 0), transfer(postddl_u, 0), transfer(postddl_ms, 0)
    write(*,'(A,1X,I0,6(1X,Z8.8))') 'DIL', case_index, &
      transfer(dil_stage, 0), transfer(0.0, 0), transfer(dil_t, 0), &
      transfer(dil_d, 0), transfer(dil_u, 0), transfer(dil_ms, 0)
    write(*,'(A,1X,I0,6(1X,Z8.8))') 'FINAL', case_index, &
      transfer(di, 0), transfer(di_s, 0), transfer(di_t, 0), &
      transfer(di_d, 0), transfer(di_u, 0), transfer(di_ms, 0)
  end do

contains

  subroutine compute_turbulent_di_chain(hk, hs, us, rt, s, msq, &
      hk_t, hk_d, hk_u, hk_ms, hs_t, hs_d, hs_u, hs_ms, &
      us_t, us_d, us_u, us_ms, rt_t, rt_u, rt_ms, m_u, m_ms, &
      di, di_s, di_t, di_d, di_u, di_ms)
    implicit none

    real, intent(in) :: hk
    real, intent(in) :: hs
    real, intent(in) :: us
    real, intent(in) :: rt
    real, intent(in) :: s
    real, intent(in) :: msq
    real, intent(in) :: hk_t
    real, intent(in) :: hk_d
    real, intent(in) :: hk_u
    real, intent(in) :: hk_ms
    real, intent(in) :: hs_t
    real, intent(in) :: hs_d
    real, intent(in) :: hs_u
    real, intent(in) :: hs_ms
    real, intent(in) :: us_t
    real, intent(in) :: us_d
    real, intent(in) :: us_u
    real, intent(in) :: us_ms
    real, intent(in) :: rt_t
    real, intent(in) :: rt_u
    real, intent(in) :: rt_ms
    real, intent(in) :: m_u
    real, intent(in) :: m_ms
    real, intent(out) :: di
    real, intent(out) :: di_s
    real, intent(out) :: di_t
    real, intent(out) :: di_d
    real, intent(out) :: di_u
    real, intent(out) :: di_ms
    real :: cf2t
    real :: cf2t_hk
    real :: cf2t_rt
    real :: cf2t_m
    real :: cf2t_u
    real :: cf2t_t
    real :: cf2t_d
    real :: cf2t_ms
    real :: di_hs
    real :: di_us
    real :: di_cf2t
    real :: grt
    real :: hmin
    real :: hm_rt
    real :: fl
    real :: fl_hk
    real :: fl_rt
    real :: tfl
    real :: dfac
    real :: df_fl
    real :: df_hk
    real :: df_rt
    real :: dd
    real :: dd_hs
    real :: dd_us
    real :: dd_s
    real :: ddl
    real :: ddl_hs
    real :: ddl_us
    real :: ddl_rt
    real :: dil
    real :: dil_hk
    real :: dil_rt

    call cft_kernel(hk, rt, msq, cf2t, cf2t_hk, cf2t_rt, cf2t_m)

    cf2t_u = cf2t_hk*hk_u + cf2t_rt*rt_u + cf2t_m*m_u
    cf2t_t = cf2t_hk*hk_t + cf2t_rt*rt_t
    cf2t_d = cf2t_hk*hk_d
    cf2t_ms = cf2t_hk*hk_ms + cf2t_rt*rt_ms + cf2t_m*m_ms

    di = (0.5*cf2t*us) * 2.0/hs
    di_hs = -(0.5*cf2t*us) * 2.0/hs**2
    di_us = (0.5*cf2t) * 2.0/hs
    di_cf2t = (0.5*us) * 2.0/hs

    di_s = 0.0
    di_u = di_hs*hs_u + di_us*us_u + di_cf2t*cf2t_u
    di_t = di_hs*hs_t + di_us*us_t + di_cf2t*cf2t_t
    di_d = di_hs*hs_d + di_us*us_d + di_cf2t*cf2t_d
    di_ms = di_hs*hs_ms + di_us*us_ms + di_cf2t*cf2t_ms
    wall_di = di
    wall_s = di_s
    wall_t = di_t
    wall_d = di_d
    wall_u = di_u
    wall_ms = di_ms

    grt = log(rt)
    hmin = 1.0 + 2.1/grt
    hm_rt = -(2.1/grt**2) / rt

    fl = (hk - 1.0)/(hmin - 1.0)
    fl_hk = 1.0/(hmin - 1.0)
    fl_rt = (-fl/(hmin - 1.0)) * hm_rt

    tfl = tanh(fl)
    dfac = 0.5 + 0.5*tfl
    df_fl = 0.5*(1.0 - tfl**2)
    df_hk = df_fl*fl_hk
    df_rt = df_fl*fl_rt

    di_s = di_s*dfac
    di_u = di_u*dfac + di*(df_hk*hk_u + df_rt*rt_u)
    di_t = di_t*dfac + di*(df_hk*hk_t + df_rt*rt_t)
    di_d = di_d*dfac + di*(df_hk*hk_d)
    di_ms = di_ms*dfac + di*(df_hk*hk_ms + df_rt*rt_ms)
    di = di*dfac
    dfac_di = di
    dfac_s = di_s
    dfac_t = di_t
    dfac_d = di_d
    dfac_u = di_u
    dfac_ms = di_ms

    dd = s**2 * (0.995-us) * 2.0/hs
    dd_hs = -s**2 * (0.995-us) * 2.0/hs**2
    dd_us = -s**2 * 2.0/hs
    dd_s = s*2.0 * (0.995-us) * 2.0/hs
    dd_stage = dd
    dd_s_stage = dd_s
    dd_u = dd_hs*hs_u + dd_us*us_u
    dd_t = dd_hs*hs_t + dd_us*us_t
    dd_d = dd_hs*hs_d + dd_us*us_d
    dd_ms = dd_hs*hs_ms + dd_us*us_ms
    di = di + dd
    di_s = dd_s
    di_u = di_u + dd_u
    di_t = di_t + dd_t
    di_d = di_d + dd_d
    di_ms = di_ms + dd_ms
    postdd_di = di
    postdd_s = di_s
    postdd_t = di_t
    postdd_d = di_d
    postdd_u = di_u
    postdd_ms = di_ms

    ddl = 0.15*(0.995-us)**2 / rt * 2.0/hs
    ddl_us = -0.15*(0.995-us)*2.0 / rt * 2.0/hs
    ddl_hs = -ddl/hs
    ddl_rt = -ddl/rt
    ddl_stage = ddl
    ddl_u = ddl_hs*hs_u + ddl_us*us_u + ddl_rt*rt_u
    ddl_t = ddl_hs*hs_t + ddl_us*us_t + ddl_rt*rt_t
    ddl_d = ddl_hs*hs_d + ddl_us*us_d
    ddl_ms = ddl_hs*hs_ms + ddl_us*us_ms + ddl_rt*rt_ms
    di = di + ddl
    di_u = di_u + ddl_u
    di_t = di_t + ddl_t
    di_d = di_d + ddl_d
    di_ms = di_ms + ddl_ms
    postddl_di = di
    postddl_s = di_s
    postddl_t = di_t
    postddl_d = di_d
    postddl_u = di_u
    postddl_ms = di_ms

    call dil_kernel(hk, rt, dil, dil_hk, dil_rt)
    dil_stage = dil
    dil_u = dil_hk*hk_u + dil_rt*rt_u
    dil_t = dil_hk*hk_t + dil_rt*rt_t
    dil_d = dil_hk*hk_d
    dil_ms = dil_hk*hk_ms + dil_rt*rt_ms
    if (dil .gt. di) then
      di = dil
      di_s = 0.0
      di_u = dil_u
      di_t = dil_t
      di_d = dil_d
      di_ms = dil_ms
    end if
  end subroutine compute_turbulent_di_chain

  subroutine dil_kernel(hk, rt, di, di_hk, di_rt)
    implicit none

    real, intent(in) :: hk
    real, intent(in) :: rt
    real, intent(out) :: di
    real, intent(out) :: di_hk
    real, intent(out) :: di_rt
    real :: hkb
    real :: den
    real :: ratio

    if (hk .lt. 4.0) then
      di = (0.00205*(4.0 - hk)**5.5 + 0.207) / rt
      di_hk = (-0.00205*5.5*(4.0 - hk)**4.5) / rt
    else
      hkb = hk - 4.0
      den = 1.0 + 0.02*hkb**2
      ratio = hkb**2 / den
      di = (-0.0016*ratio + 0.207) / rt
      di_hk = (-0.0016*2.0*hkb*(1.0/den - 0.02*hkb**2/den**2)) / rt
    end if

    di_rt = -di / rt
  end subroutine dil_kernel

end program di_turbulent_parity_driver

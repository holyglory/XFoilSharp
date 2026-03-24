program di_dfac_parity_driver
  use bl_common_kernels, only: cft_kernel
  implicit none

  integer :: case_count
  integer :: case_index
  real :: hk
  real :: hs
  real :: us
  real :: rt
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
  real :: grt
  real :: hmin
  real :: hm_rt
  real :: fl
  real :: dfac
  real :: df_hk
  real :: df_rt
  real :: df_term_d
  real :: di
  real :: di_s
  real :: di_t
  real :: di_d
  real :: di_u
  real :: di_ms

  read(*,*) case_count
  write(*,'(I8)') case_count

  do case_index = 1, case_count
    read(*,*) hk, hs, us, rt, msq
    read(*,*) hk_t, hk_d, hk_u, hk_ms
    read(*,*) hs_t, hs_d, hs_u, hs_ms
    read(*,*) us_t, us_d, us_u, us_ms
    read(*,*) rt_t, rt_u, rt_ms
    read(*,*) m_u, m_ms

    call compute_di_dfac(hk, hs, us, rt, msq, &
      hk_t, hk_d, hk_u, hk_ms, hs_t, hs_d, hs_u, hs_ms, &
      us_t, us_d, us_u, us_ms, rt_t, rt_u, rt_ms, m_u, m_ms, &
      grt, hmin, hm_rt, fl, dfac, df_hk, df_rt, df_term_d, &
      di, di_s, di_t, di_d, di_u, di_ms)

    write(*,'(A,1X,I0,8(1X,Z8.8))') 'TERMS', case_index, &
      transfer(grt, 0), transfer(hmin, 0), transfer(hm_rt, 0), transfer(fl, 0), &
      transfer(dfac, 0), transfer(df_hk, 0), transfer(df_rt, 0), transfer(df_term_d, 0)
    write(*,'(A,1X,I0,6(1X,Z8.8))') 'FINAL', case_index, &
      transfer(di, 0), transfer(di_s, 0), transfer(di_t, 0), &
      transfer(di_d, 0), transfer(di_u, 0), transfer(di_ms, 0)
  end do

contains

  subroutine compute_di_dfac(hk, hs, us, rt, msq, &
      hk_t, hk_d, hk_u, hk_ms, hs_t, hs_d, hs_u, hs_ms, &
      us_t, us_d, us_u, us_ms, rt_t, rt_u, rt_ms, m_u, m_ms, &
      grt, hmin, hm_rt, fl, dfac, df_hk, df_rt, df_term_d, &
      di, di_s, di_t, di_d, di_u, di_ms)
    implicit none

    real, intent(in) :: hk
    real, intent(in) :: hs
    real, intent(in) :: us
    real, intent(in) :: rt
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
    real, intent(out) :: grt
    real, intent(out) :: hmin
    real, intent(out) :: hm_rt
    real, intent(out) :: fl
    real, intent(out) :: dfac
    real, intent(out) :: df_hk
    real, intent(out) :: df_rt
    real, intent(out) :: df_term_d
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
    real :: fl_hk
    real :: fl_rt
    real :: tfl
    real :: df_fl

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
    di_t = di_hs*hs_t + di_us*us_t + di_cf2t*cf2t_t
    di_d = di_hs*hs_d + di_us*us_d + di_cf2t*cf2t_d
    di_u = di_hs*hs_u + di_us*us_u + di_cf2t*cf2t_u
    di_ms = di_hs*hs_ms + di_us*us_ms + di_cf2t*cf2t_ms

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
    df_term_d = df_hk*hk_d

    di_s = di_s*dfac
    di_u = di_u*dfac + di*(df_hk*hk_u + df_rt*rt_u)
    di_t = di_t*dfac + di*(df_hk*hk_t + df_rt*rt_t)
    di_d = di_d*dfac + di*(df_hk*hk_d)
    di_ms = di_ms*dfac + di*(df_hk*hk_ms + df_rt*rt_ms)
    di = di*dfac
  end subroutine compute_di_dfac

end program di_dfac_parity_driver

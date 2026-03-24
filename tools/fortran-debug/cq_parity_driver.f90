program cq_parity_driver
  implicit none

  integer :: case_count
  integer :: case_index
  integer :: ityp
  real :: hk
  real :: hs
  real :: us
  real :: h
  real :: rt
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
  real :: h_t
  real :: h_d
  real :: rt_t
  real :: rt_u
  real :: rt_ms
  real :: gacon
  real :: gbcon
  real :: gccon
  real :: ctcon
  real :: hkc
  real :: hkc_hk
  real :: hkc_rt
  real :: hkb
  real :: usb
  real :: cqnum
  real :: cqden
  real :: cqrat
  real :: cq
  real :: half
  real :: cq_hs
  real :: cq_us
  real :: cq_hk
  real :: cq_h
  real :: cq_rt
  real :: cq_hk_term1
  real :: cq_hk_term2
  real :: cq_hk_term3
  real :: cq_term_hs_t
  real :: cq_term_us_t
  real :: cq_term_hk_t
  real :: cq_term_h_t
  real :: cq_term_rt_t
  real :: cq_term_hs_d
  real :: cq_term_us_d
  real :: cq_term_hk_d
  real :: cq_term_h_d
  real :: cq_term_hs_u
  real :: cq_term_us_u
  real :: cq_term_hk_u
  real :: cq_term_rt_u
  real :: cq_term_hs_ms
  real :: cq_term_us_ms
  real :: cq_term_hk_ms
  real :: cq_term_rt_ms
  real :: cq_t
  real :: cq_d
  real :: cq_u
  real :: cq_ms

  gacon = 6.70
  gbcon = 0.75
  gccon = 18.0
  ctcon = 0.5 / (gacon * gacon * gbcon)
  half = 0.5

  read(*,*) case_count
  write(*,'(I8)') case_count

  do case_index = 1, case_count
    read(*,*) ityp, hk, hs, us, h, rt
    read(*,*) hk_t, hk_d, hk_u, hk_ms
    read(*,*) hs_t, hs_d, hs_u, hs_ms
    read(*,*) us_t, us_d, us_u, us_ms
    read(*,*) h_t, h_d
    read(*,*) rt_t, rt_u, rt_ms

    hkc = hk - 1.0
    hkc_hk = 1.0
    hkc_rt = 0.0
    if (ityp .eq. 2) then
      hkc = hk - 1.0 - gccon / rt
      hkc_hk = 1.0
      hkc_rt = gccon / rt**2
      if (hkc .lt. 0.01) then
        hkc = 0.01
        hkc_hk = 0.0
        hkc_rt = 0.0
      end if
    end if

    hkb = hk - 1.0
    if (hkb .lt. 0.01) hkb = 0.01

    usb = 1.0 - us
    if (usb .lt. 0.01) usb = 0.01

    cqnum = ctcon * hs * hkb * hkc**2
    cqden = usb * h * hk**2
    cqrat = cqnum / cqden
    cq = sqrt(cqrat)

    write(*,'(A,1X,I0,1X,I0,7(1X,Z8.8))') 'TERMS', case_index, ityp, &
      transfer(hkc, 0), transfer(hkb, 0), transfer(usb, 0), &
      transfer(cqnum, 0), transfer(cqden, 0), transfer(cqrat, 0), transfer(cq, 0)

    cq_hs = ctcon * hkb * hkc**2 / (usb * h * hk**2) * half / cq
    cq_us = ctcon * hs * hkb * hkc**2 / (usb * h * hk**2) / usb * half / cq
    cq_hk = ctcon * hs * hkc**2 / (usb * h * hk**2) * half / cq &
          - ctcon * hs * hkb * hkc**2 / (usb * h * hk**3) * 2.0 * half / cq &
          + ctcon * hs * hkb * hkc / (usb * h * hk**2) * 2.0 * half / cq * hkc_hk
    cq_rt = ctcon * hs * hkb * hkc / (usb * h * hk**2) * 2.0 * half / cq * hkc_rt
    cq_h = -ctcon * hs * hkb * hkc**2 / (usb * h * hk**2) / h * half / cq

    cq_term_hs_t = cq_hs * hs_t
    cq_term_us_t = cq_us * us_t
    cq_term_hk_t = cq_hk * hk_t
    cq_term_h_t = cq_h * h_t
    cq_term_rt_t = cq_rt * rt_t
    cq_term_hs_d = cq_hs * hs_d
    cq_term_us_d = cq_us * us_d
    cq_term_hk_d = cq_hk * hk_d
    cq_term_h_d = cq_h * h_d
    cq_term_hs_u = cq_hs * hs_u
    cq_term_us_u = cq_us * us_u
    cq_term_hk_u = cq_hk * hk_u
    cq_term_rt_u = cq_rt * rt_u
    cq_term_hs_ms = cq_hs * hs_ms
    cq_term_us_ms = cq_us * us_ms
    cq_term_hk_ms = cq_hk * hk_ms
    cq_term_rt_ms = cq_rt * rt_ms

    cq_t = cq_hs * hs_t + cq_us * us_t + cq_hk * hk_t
    cq_t = cq_t + cq_h * h_t + cq_rt * rt_t
    cq_d = cq_hs * hs_d + cq_us * us_d + cq_hk * hk_d
    cq_d = cq_d + cq_h * h_d
    cq_u = cq_hs * hs_u + cq_us * us_u + cq_hk * hk_u
    cq_u = cq_u + cq_rt * rt_u
    cq_ms = cq_hs * hs_ms + cq_us * us_ms + cq_hk * hk_ms
    cq_ms = cq_ms + cq_rt * rt_ms

    cq_hk_term1 = ctcon * hs * hkc**2 / (usb * h * hk**2)
    cq_hk_term2 = ctcon * hs * hkb * hkc**2 / (usb * h * hk**3) * 2.0
    cq_hk_term3 = ctcon * hs * hkb * hkc / (usb * h * hk**2) * 2.0

    write(*,'(A,1X,I0,1X,I0,27(1X,Z8.8))') 'DTERM', case_index, ityp, &
      transfer(cq_hs, 0), transfer(cq_us, 0), transfer(cq_hk, 0), transfer(cq_h, 0), transfer(cq_rt, 0), &
      transfer(cq_hk_term1, 0), transfer(cq_hk_term2, 0), transfer(cq_hk_term3, 0), &
      transfer(cq_term_hs_t, 0), transfer(cq_term_us_t, 0), transfer(cq_term_hk_t, 0), &
      transfer(cq_term_h_t, 0), transfer(cq_term_rt_t, 0), &
      transfer(cq_term_hs_d, 0), transfer(cq_term_us_d, 0), transfer(cq_term_hk_d, 0), transfer(cq_term_h_d, 0), &
      transfer(cq_term_hs_u, 0), transfer(cq_term_us_u, 0), transfer(cq_term_hk_u, 0), transfer(cq_term_rt_u, 0), &
      transfer(cq_term_hs_ms, 0), transfer(cq_term_us_ms, 0), transfer(cq_term_hk_ms, 0), transfer(cq_term_rt_ms, 0), &
      transfer(cq_t, 0), transfer(cq_d, 0)

    write(*,'(A,1X,I0,1X,I0,5(1X,Z8.8))') 'FINAL', case_index, ityp, &
      transfer(cq, 0), transfer(cq_t, 0), transfer(cq_d, 0), transfer(cq_u, 0), transfer(cq_ms, 0)
  end do
end program cq_parity_driver

<?php

function bar($y) {
  if ($y) {
    $z = "foo";
  } else {
    $z = "bar";
  }

  if ($z) {
    echo "bla";
  } else {
    echo "bal";
  }

  return $z;
}

function foo($x) {
  if ($x) {
    $y = bar($x);
  } else {
    $y = "";
  }

  echo /*|string|*/$y;
}
